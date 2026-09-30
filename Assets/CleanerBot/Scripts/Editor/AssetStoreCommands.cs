using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Reflection;
using System.Text;
using Unity.Pipeline.Commands;
using UnityEditor;
using UnityEngine;

namespace CleanerBot.EditorTools
{
    /// <summary>
    /// EXPERIMENTAL: list / download / import the signed-in user's Asset Store purchases ("My Assets")
    /// from the CLI. Uses the Package Manager's INTERNAL services via reflection (UnityEditor.PackageManager.UI.Internal),
    /// so it may break with a future Editor version. Network calls are asynchronous: start with one command,
    /// read the result with a second one.
    /// </summary>
    public static class AssetStoreCommands
    {
        const string Ns = "UnityEditor.PackageManager.UI.Internal.";
        const BindingFlags Any = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static;

        // --- async results (live in the Editor domain until the next reload) ---
        static readonly List<string> s_Purchases = new List<string>();
        static string s_ListState = "idle";
        static long s_ListTotal;

        // ---------- reflection helpers ----------
        static Type T(string name) => AppDomain.CurrentDomain.GetAssemblies()
            .Where(a => a.GetName().Name.StartsWith("UnityEditor"))
            .Select(a => a.GetType(name, false)).FirstOrDefault(t => t != null);

        static object Service(string typeName)
        {
            var containerT = T(Ns + "ServicesContainer") ?? throw new Exception("ServicesContainer not found");
            var instance = containerT.GetProperty("instance", Any | BindingFlags.FlattenHierarchy)?.GetValue(null)
                           ?? throw new Exception("ServicesContainer.instance is null");
            var svcT = T(Ns + typeName) ?? throw new Exception(typeName + " not found");
            var resolve = containerT.GetMethod("Resolve", Any).MakeGenericMethod(svcT);
            return resolve.Invoke(instance, null) ?? throw new Exception(typeName + " not registered");
        }

        /// <summary>Wraps an Action&lt;object&gt; as Action&lt;T&gt; for a runtime type T.</summary>
        class Relay<TArg>
        {
#pragma warning disable CS0649 // assigned via reflection
            public Action<object> Target;
#pragma warning restore CS0649
            public void Call(TArg arg) => Target(arg);
        }

        static Delegate MakeAction(Type argType, Action<object> target)
        {
            var relayT = typeof(Relay<>).MakeGenericType(argType);
            var relay = Activator.CreateInstance(relayT);
            relayT.GetField("Target").SetValue(relay, target);
            return Delegate.CreateDelegate(typeof(Action<>).MakeGenericType(argType), relay, relayT.GetMethod("Call"));
        }

        static object Get(object o, string member)
        {
            if (o == null) return null;
            var t = o.GetType();
            var f = t.GetField(member, Any);
            if (f != null) return f.GetValue(o);
            var p = t.GetProperty(member, Any);
            return p?.GetValue(o);
        }

        // ---------- commands ----------

        [CliCommand("cb_store_info", "EXPERIMENTAL: Asset Store sign-in state, download cache folder and already-downloaded packages.")]
        public static string Info()
        {
            var sb = new StringBuilder();
            try
            {
                var connectT = T("UnityEditor.Connect.UnityConnect");
                var connect = connectT?.GetProperty("instance", Any)?.GetValue(null);
                sb.AppendLine($"loggedIn={Get(connect, "loggedIn")} user={Get(Get(connect, "userInfo"), "userName")}");
            }
            catch (Exception e) { sb.AppendLine("loggedIn=? " + e.Message); }

            try
            {
                var proxy = Service("AssetStoreCachePathProxy");
                var cfg = proxy.GetType().GetMethod("GetConfig", Any).Invoke(proxy, null);
                sb.AppendLine($"cache={Get(cfg, "path")} source={Get(cfg, "source")} status={Get(cfg, "status")}");
            }
            catch (Exception e) { sb.AppendLine("cache=? " + e.Message); }

            try
            {
                var utilsT = T(Ns + "AssetStoreUtils");
                var utils = Service("AssetStoreUtils");
                var list = (Array)utilsT.GetMethod("GetLocalPackageList", Any).Invoke(utils, null);
                sb.AppendLine($"downloaded={list?.Length ?? 0}");
                if (list != null)
                    foreach (var p in list)
                        sb.AppendLine($"  {Get(p, "title")} | {Get(p, "packagePath")}");
            }
            catch (Exception e) { sb.AppendLine("downloaded=? " + (e.InnerException ?? e).Message); }
            return sb.ToString().TrimEnd();
        }

        [CliCommand("cb_store_list", "EXPERIMENTAL: start fetching 'My Assets' (purchases). Read the result with cb_store_list_result.")]
        public static string List(
            [CliArg("search", "Optional search text")] string search = "",
            [CliArg("limit", "Max items")] int limit = 100)
        {
            try
            {
                var api = Service("AssetStoreRestAPI");
                var argsT = T(Ns + "PurchasesQueryArgs");
                var args = argsT.GetConstructors(Any)[0].Invoke(new object[] { 0, limit, search ?? "", null });
                var purchasesT = T(Ns + "AssetStorePurchases");
                var errorT = T(Ns + "UIError");

                s_Purchases.Clear();
                s_ListState = "fetching";
                var ok = MakeAction(purchasesT, res =>
                {
                    s_ListTotal = Convert.ToInt64(Get(res, "total"));
                    foreach (var info in (IEnumerable)Get(res, "list"))
                    {
                        var tags = Get(info, "tags") as string[];
                        s_Purchases.Add($"{Get(info, "productId")} | {Get(info, "displayName")}" +
                                        (tags != null && tags.Length > 0 ? " | " + string.Join(",", tags) : "") +
                                        (Equals(Get(info, "isHidden"), true) ? " | hidden" : ""));
                    }
                    s_ListState = "done";
                });
                var err = MakeAction(errorT, e => s_ListState = "error: " + Get(e, "message"));

                api.GetType().GetMethod("GetPurchases", Any).Invoke(api, new object[] { args, ok, err });
                return "fetching purchases… call cb_store_list_result";
            }
            catch (Exception e)
            {
                s_ListState = "error: " + (e.InnerException ?? e).Message;
                return s_ListState;
            }
        }

        [CliCommand("cb_store_list_result", "EXPERIMENTAL: result of the last cb_store_list.")]
        public static string ListResult()
        {
            var sb = new StringBuilder();
            sb.AppendLine($"state={s_ListState} total={s_ListTotal} returned={s_Purchases.Count}");
            foreach (var p in s_Purchases) sb.AppendLine("  " + p);
            return sb.ToString().TrimEnd();
        }

        [CliCommand("cb_store_download", "EXPERIMENTAL: download an owned Asset Store package (product id from cb_store_list) into the Asset Store cache.")]
        public static string Download([CliArg("product_id", "Asset Store product id")] long productId)
        {
            try
            {
                var dm = Service("AssetStoreDownloadManager");
                dm.GetType().GetMethod("Download", Any, null, new[] { typeof(long) }, null).Invoke(dm, new object[] { productId });
                return "download requested: " + productId + " (poll cb_store_download_status)";
            }
            catch (Exception e) { return "error: " + (e.InnerException ?? e).Message; }
        }

        [CliCommand("cb_store_download_status", "EXPERIMENTAL: state/progress of a download started with cb_store_download.")]
        public static string DownloadStatus([CliArg("product_id", "Asset Store product id")] long productId)
        {
            try
            {
                var dm = Service("AssetStoreDownloadManager");
                var op = dm.GetType().GetMethod("GetDownloadOperation", Any).Invoke(dm, new object[] { (long?)productId });
                if (op == null) return "no download operation (not started, or finished and cleared) — check cb_store_info";
                return $"state={Get(op, "state")} progress={Get(op, "progressPercentage")} inProgress={Get(op, "isInProgress")} " +
                       $"error={Get(Get(op, "errorMessage"), "message") ?? Get(op, "errorMessage")} path={Get(op, "packageOldPath") ?? ""}";
            }
            catch (Exception e) { return "error: " + (e.InnerException ?? e).Message; }
        }

        [CliCommand("cb_store_import", "EXPERIMENTAL: import an already-downloaded Asset Store package (matched by title substring) into the project, without the import dialog.")]
        public static string Import([CliArg("title", "Package title (or part of it) as shown by cb_store_info")] string title)
        {
            try
            {
                var utilsT = T(Ns + "AssetStoreUtils");
                var utils = Service("AssetStoreUtils");
                var list = (Array)utilsT.GetMethod("GetLocalPackageList", Any).Invoke(utils, null);
                foreach (var p in list)
                {
                    var t = Get(p, "title") as string ?? "";
                    if (t.IndexOf(title, StringComparison.OrdinalIgnoreCase) < 0) continue;
                    var path = Get(p, "packagePath") as string;
                    UnityEditor.AssetPackage.Package.Import(path, false);
                    return $"importing '{t}' from {path} (watch recompile_status / console)";
                }
                return $"error: no downloaded package matches '{title}'";
            }
            catch (Exception e) { return "error: " + (e.InnerException ?? e).Message; }
        }
    }
}
