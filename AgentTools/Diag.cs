using UnityEngine;
using UnityEngine.AI;

public static class Diag
{
    public static string Run()
    {
        var r = Object.FindAnyObjectByType<CleanerBot.RobotController>();
        var a = r.GetComponent<NavMeshAgent>();
        return $"time={Time.time:0.00} frame={Time.frameCount} dt={Time.deltaTime:0.000} ts={Time.timeScale} paused={UnityEditor.EditorApplication.isPaused} " +
               $"agentEnabled={a.enabled} stopped={a.isStopped} speed={a.speed} vel={a.velocity} desired={a.desiredVelocity} hasPath={a.hasPath} pending={a.pathPending} status={a.pathStatus} " +
               $"corners={a.path.corners.Length} upd={a.updatePosition} steering={a.steeringTarget} pos={r.transform.position} nextPos={a.nextPosition}";
    }
}
