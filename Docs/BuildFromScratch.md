# CleanerBot — 처음부터 다시 만드는 순서 (최종 설계 기준)

진공청소 로봇 시뮬레이터를 **Unity CLI + Pipeline 패키지**로 Editor를 조작해 만드는 과정입니다.
지금까지 작업하면서 고친 내용(가구 방향, 문 위치, 커버리지 허용 오차 등)을 처음부터 반영한 **최종 설계 기준**으로 정리했습니다.

- 환경: Windows, Unity 6000.6.3f1, Unity CLI 1.0.0-beta.11 (`C:\Users\proon\AppData\Local\Unity\bin` — PATH에 추가)
- 프로젝트 경로: `D:\UnityAIProjs\DigitalTwin\CleanerBot`
- 기본 작업 방식: C# 코드 작성 → `dryrun.ps1`로 컴파일 확인 → AssetDatabase.Refresh(컴파일) → `unity command <cb_명령>` 실행 → 스크린샷·헤드리스 시뮬레이션으로 확인 → git 커밋

---

## 1단계. 프로젝트 생성과 에이전트 작업 환경

1. URP 3D 템플릿으로 프로젝트 생성: `unity projects new CleanerBot ...` (Unity 6000.6.3f1)
2. 패키지 추가 (`Packages/manifest.json`)
   - `com.unity.ai.navigation` 2.0.14 (NavMeshSurface)
   - `com.unity.pipeline` 0.8.0-exp.1 (`unity pipeline install`) → `[CliCommand]`로 만든 C# 정적 메서드를 `unity command`로 호출 가능
3. Editor 열기: `unity open` → `unity status`로 연결 확인
4. `AgentTools/` 도우미 스크립트
   - `cb.ps1`: `unity command <명령> --no-banner` 래퍼 (UTF-8 출력, PATH 설정)
   - `dryrun.ps1`: `run_script --dry_run`으로 `Assets/CleanerBot` 전체 컴파일 확인 (도메인 리로드 없이)
   - `Refresh.cs`: `run_script`로 AssetDatabase.Refresh 실행 → 새 코드 컴파일
   - `focus_unity.ps1`: 플레이 모드는 Unity 창이 포그라운드일 때만 진행되므로 창을 앞으로 가져옴
5. git 초기화, `.gitignore`에 서드파티 에셋 폴더(`Assets/ithappy` 등) 추가

> 주의: 한 번의 CLI 호출은 60초 안에 끝나게 나눠 실행. 한글이 들어간 파일은 UTF-8로 쓰기.

## 2단계. 데이터 모델 — 집 레이아웃과 프리셋

- `HouseLayout`: 크기, 벽(`WallSpec` + 문 구간), 가구(`FurnitureSpec`: kind, 위치, rotY, size), 로봇 시작 위치·방향(= 충전대)
- `HousePresets` (최종 좌표로 바로 작성)
  - **Apartment_A** 10×8 m, 방 4개. 거실↔서재 문은 **x 0.6–1.6** (TV장 x 1.7–3.3 옆), 서재 화분 (0.4, 6.4), 서재 1인 소파 (5.47, 5.8, −90°), 충전대 (4.6, 4.65) 180°
  - **Studio_B** 7×5 m 원룸 + 욕실, 충전대 (0.35, 2.3) 90°
  - **House_C** 12×9 m 방 5개(침실 2개), 충전대 (6.5, 4.65) 180°
- 가구 규칙: 로컬 +Z가 앞면, 벽에 붙는 가구는 뒷면(−Z)을 벽 쪽으로. 문 앞 1 m 안에는 가구를 두지 않음

## 3단계. 집 생성기와 가구 (3D 모델 우선, 없으면 기본 도형)

- `HouseGenerator.Build(layout, materials, catalog)`: 바닥, 벽(문 구간 제외), 가구, 충전대(콜라이더 없음) 생성 → `HouseInstance`
- `FurnitureFactory.Create`: 카탈로그에 모델이 있으면 모델, 없으면 기본 도형
  - 모델 배치 순서: 원점에 생성 → `yawOffset` 회전 → (주방 수납장은 폭만큼 반복) → 바닥에 맞춤 → 기준 가장자리 정렬(Back/Front/Center) → `depthOffset`만큼 앞으로 → MeshCollider 추가(NavMesh용)
  - 다리가 있는 가구(테이블·의자)는 밑으로 로봇이 지나갈 수 있음
- `FurnitureCatalog` (ScriptableObject): kind → prefab, yawOffset, depthOffset, align, tileAlongX
- 에셋이 준비되기 전에는 카탈로그 없이 기본 도형으로 진행 가능 (8단계에서 모델로 교체)

## 4단계. NavMesh와 로봇

- 로봇 전용 에이전트 id 0 "CleanerBot": 반경 0.17, 높이 0.1, climb 0.02, voxel 0.03
- NavMeshSurface는 PhysicsColliders 기준, 가구에 NavMeshModifier(NotWalkable)
- `RobotFactory`/`RobotController`: 원판형 로봇(본체, 범퍼, 라이다, 경로 표시선)
- CLI: `cb_setup_scene [preset]` 하나로 조명·카메라·집·NavMesh 굽기·로봇 배치까지 만들고 씬 저장
  - 보조: `cb_configure_agent`, `cb_rebake`, `cb_navmesh_info`, `cb_frame_camera`, `cb_robot_goto/mode/status`

## 5단계. 청소 커버리지 (지나간 곳 칠하기)

- `CoverageGrid`: 5 cm 셀. 청소 가능 영역 = NavMesh 위에 있는 셀(중심이 메시에서 **5 mm 이내**) → 충전대 기준 flood fill → 브러시 반경 0.16 m만큼 확장
- 셀마다 지나간 횟수를 셈 → 평균 통과 횟수, 중복률 계산
- `CoveragePainter` + `CoverageOverlay.shader`: 월드 XZ 좌표로 바닥 위에 반투명 색 오버레이
- CLI: `cb_add_coverage`, `cb_coverage`, `cb_coverage_reset`, `cb_coverage_export`

## 6단계. 고정 시간 단위 시뮬레이션과 경로 계획

- `RobotSim`: 결정론적 차동 구동 모델. `SimulationCore`: dt 0.05초 고정, 플레이어 루프와 무관 → 같은 입력이면 같은 결과
- 계획기: `FrontierPlanner`(가장 가까운 미청소 영역을 BFS로 찾고, 청소한 곳에 맞닿는 레인 우선, 직선 연장), `RandomPlanner`(비교용)
- `SimulationRunner`(플레이 모드, 속도 배율), `HeadlessSim`(에디터에서 즉시 끝까지 실행)
- CLI: `cb_add_simulation`, `cb_sim_run`, `cb_sim_compare`, `cb_sim_start/speed/pause/advance/status`

## 7단계. 배터리와 충전대 복귀

- `BatterySim`: 사용 15분, 완충 30분, 예비 5%, 복귀 거리 안전계수 1.25, 100% 충전 후 재개
- 작업 상태: 청소 → (부족) 충전대 복귀 → 충전 → 이어서 청소할 위치로 이동 → 청소 재개 → 완료 시 충전대로 돌아가 Docked
- CLI: `cb_battery`. 시뮬레이션 결과에 배터리·충전 횟수·충전 시간 포함

## 8단계. Asset Store 보유 에셋으로 가구 교체

1. 보유 에셋 조회·다운로드 명령(`AssetStoreCommands.cs`, Package Manager 내부 서비스를 리플렉션으로 호출)
   - `cb_store_list <검색어>` → `cb_store_download 260522` (Cute Furniture FREE)
2. `unity assets inspect <pkg>`로 내용 확인 후 가져오기
   - 서명 없는 패키지는 "Missing signature" 창이 뜸 → `ImportDelayed.cs`로 예약 가져오기 후 **사용자 승인을 받고** "Import Anyway"
3. `cb_setup_catalog`: 모델 Read/Write 켜기(런타임 MeshCollider용) + 카탈로그 생성 + AppController 연결. **최종 매핑**

| kind | 모델 | 정렬 | yaw | 벽에서 띄움 |
|---|---|---|---|---|
| sofa | Couch_08 | Back | 0 | 0.085 |
| armchair | Armchair_02 | Back | 0 | 0 |
| bookshelf / wardrobe | Closet_01 | Back | 0 | 0.045 / 0 |
| kitchen_counter | Kitchen_D_01 (반복) | Back | 0 | 0.01 |
| fridge | Fridge_01 | Back | 0 | 0.1 |
| coffee_table / dining_table | Coffee_Table_03 / Kitchen_Table_09 | Center | 180 | 0 |
| desk / chair | Work_Table_06 / Chair_17 | Back / Center | 180 | 0 |
| bed / nightstand | Bed_07 / Nightstand_02 | Front / Back | 180 | 0 |
| plant | Plants_15 | Center | 180 | 0 |
| tv_stand | (모델 없음 → 기본 도형) | | | |

4. `cb_setup_scene`을 다시 실행해 모델이 들어간 씬 생성

## 9단계. UI Toolkit 화면과 한국어/영어

- `AppController`: 런타임 집 전환(재생성 + NavMesh 재굽기 + 커버리지 재설정), Start/Pause/Reset, 속도, 계획기 비교, 배터리, 3D 모델 사용 토글
- UI 파일(`Assets/CleanerBot/UI/`): uxml, uss, tss, PanelSettings, `CleanerBotUI.cs`, `TimelineChart.cs`(Painter2D 커버리지 그래프)
  - 왼쪽 패널(ScrollView): 집 선택, 청소 시작, 속도, 요약, 설정(언어·배터리·가구 모델)
  - 카메라: 집 전체를 UI 패널 오른쪽 영역에 맞춤(lens shift)
- 다국어: `Loc.cs`(영어 문장이 키, 인덱서 초기화로 중복 키 방지), 설정의 [English]/[한국어], PlayerPrefs에 저장
- 한글 폰트: Noto Sans KR(OFL)을 fallback으로 등록 (`cb_setup_font`)
- CLI: `cb_add_ui`, `cb_app_load_house`, `cb_app_compare`

## 10단계. 검증과 기록

1. 컴파일: `dryrun.ps1` → Refresh → `editor_status`가 ready인지 확인
2. 씬 재생성: `cb_setup_scene` (필요하면 `--preset Studio_B`, `House_C`를 임시 경로로 만들어 확인 후 삭제, 빌드 설정에서도 제거)
3. 화면 확인: `capture_game_view --source camera --save_path ...` (Assets 아래에 저장되므로 `Screenshots/`로 옮김)
   - 가구 앞면이 방 안쪽을 보는지, 문이 막히지 않았는지 확인
4. 시뮬레이션: `cb_sim_compare` (기준: Apartment_A Frontier 99.5%, Random은 60분에 약 53%)
5. 플레이 모드 확인: `editor_play` → `focus_unity.ps1` → UI에서 집 전환·언어 전환·배터리 동작 확인
6. git 커밋, 진행 문서(`claude/CleanerBot-progress.md`) 업데이트

> 씬에서 직접 가구를 고친 경우: 바로 커밋해 백업 → `DiffFurniture2.cs`로 프리셋·카탈로그와 비교 → 모든 집에 해당하는 수정은 카탈로그(yaw/depth), 한 곳만 해당하는 수정은 `HousePresets`에 반영 → `cb_setup_catalog` → `cb_setup_scene`

---

### 다음 작업 후보
- 방 단위 지그재그(보스트로피돈) 계획기
- 실제 평면도용 프리셋 JSON
- TV장·주방 모델 보강, 로봇청소기 모델
