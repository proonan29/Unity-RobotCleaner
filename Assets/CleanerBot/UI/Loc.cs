using System;
using System.Collections.Generic;
using System.Globalization;
using UnityEngine;

namespace CleanerBot.UI
{
    public enum Language { English = 0, Korean = 1 }

    /// <summary>
    /// Minimal EN/KO localization. Keys are the English strings (also used as-is in UXML),
    /// so the English UI needs no lookup and UI Builder stays readable.
    /// The selected language is stored in PlayerPrefs.
    /// </summary>
    public static class Loc
    {
        const string PrefKey = "CleanerBot.Language";
        static Language? _current;

        public static event Action Changed;

        public static Language Current
        {
            get
            {
                if (_current == null)
                {
                    int fallback = Application.systemLanguage == SystemLanguage.Korean ? 1 : 0;
                    _current = (Language)PlayerPrefs.GetInt(PrefKey, fallback);
                }
                return _current.Value;
            }
            set
            {
                if (_current == value) return;
                _current = value;
                PlayerPrefs.SetInt(PrefKey, (int)value);
                PlayerPrefs.Save();
                Changed?.Invoke();
            }
        }

        public static string T(string en)
        {
            if (string.IsNullOrEmpty(en) || Current == Language.English) return en;
            return Ko.TryGetValue(en, out var ko) ? ko : en;
        }

        public static string F(string enFormat, params object[] args) =>
            string.Format(CultureInfo.InvariantCulture, T(enFormat), args);

        static readonly Dictionary<string, string> Ko = new Dictionary<string, string>
        {
            // side panel
            ["Robot vacuum simulator"] = "로봇청소기 시뮬레이터",
            ["Settings"] = "설정",
            ["LANGUAGE"] = "언어",
            ["HOUSE"] = "집 선택",
            ["PLANNER"] = "경로 계획 방식",
            ["CONTROL"] = "제어",
            ["Start"] = "청소 시작",
            ["Pause"] = "일시정지",
            ["Resume"] = "계속",
            ["Reset"] = "초기화",
            ["SIMULATION SPEED"] = "시뮬레이션 속도",
            ["Show remaining area"] = "남은 영역 표시",
            ["LIVE"] = "실시간",
            ["Elapsed (sim)"] = "경과 시간 (시뮬)",
            ["Distance"] = "이동 거리",
            ["Average passes"] = "평균 청소 횟수",
            ["Cleanable area"] = "청소 가능 면적",
            ["Summary"] = "요약",
            ["Compare planners"] = "계획 방식 비교",
            // status
            ["Ready — {0}"] = "준비됨 — {0}",
            ["Cleaning ({0})"] = "청소 중 ({0})",
            ["Paused"] = "일시정지됨",
            ["Finished — {0}"] = "완료 — {0}",
            ["Comparing planners…"] = "계획 방식 비교 중…",
            ["target coverage"] = "목표 커버리지 달성",
            ["complete"] = "청소 완료",
            ["time limit"] = "시간 제한 도달",
            ["stuck"] = "이동 불가",
            // summary
            ["Close"] = "닫기",
            ["Cleaning summary"] = "청소 요약",
            ["Planner comparison"] = "계획 방식 비교",
            ["COVERAGE OVER TIME"] = "시간별 커버리지",
            ["{0} · planner {1} · {2}"] = "{0} · 계획 {1} · {2}",
            ["{0} · headless run, limit {1} min"] = "{0} · 고속 계산, 제한 {1}분",
            ["Coverage"] = "커버리지",
            ["Time (sim)"] = "소요 시간",
            ["Time to 90 %"] = "90% 도달",
            ["Time to 95 %"] = "95% 도달",
            ["Avg passes"] = "평균 횟수",
            ["Overlap"] = "중복 청소",
            ["Turning"] = "회전량",
            ["Cleaned"] = "청소 면적",
            ["Goals"] = "목표 수",
            ["{0} ({1} failed)"] = "{0} (실패 {1})",
            ["{0} rev"] = "{0}바퀴",
            ["{0}m"] = "{0}분",
            // comparison table
            ["COVERAGE"] = "커버리지",
            ["TIME"] = "시간",
            ["TO 90 %"] = "90% 도달",
            ["DISTANCE"] = "이동 거리",
            ["PASSES"] = "평균 횟수",
            // battery / dock
            ["BATTERY"] = "배터리",
            ["Simulate battery"] = "배터리 시뮬레이션",
            ["Capacity"] = "사용 시간",
            ["Full charge time"] = "완충 시간",
            ["{0} min"] = "{0}분",
            ["Applies from the next Start."] = "다음 청소 시작부터 적용됩니다.",
            ["Battery"] = "배터리",
            ["Off"] = "끔",
            ["Returning to dock (done)"] = "충전대로 복귀 중 (청소 완료)",
            ["Returning to charge ({0} %)"] = "충전하러 복귀 중 ({0} %)",
            ["Charging {0} %"] = "충전 중 {0} %",
            ["Returning to resume point"] = "이어서 청소할 위치로 이동 중",
            ["battery depleted"] = "배터리 방전",
            ["dock unreachable"] = "충전대에 갈 수 없음",
            ["Charges"] = "충전 횟수",
            ["Charging time"] = "충전 시간",
            ["Lowest battery"] = "최저 배터리",
            ["CHARGES"] = "충전",
            // planners
            ["Frontier"] = "프론티어",
            ["Random"] = "랜덤",
            // houses (display names from HousePresets)
            ["Apartment A (4 rooms, 80 m²)"] = "아파트 A (방 4개, 80 m²)",
            ["Studio B (open room + bath, 35 m²)"] = "원룸 B (원룸 + 욕실, 35 m²)",
            ["House C (5 rooms, 108 m²)"] = "주택 C (방 5개, 108 m²)",
        };
    }
}
