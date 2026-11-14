# PARKSIDE 개발 인수인계

현재 소스 기준이다. PC에서 Unity로 편집하고, Pleos 입력을 확인할 때 Android APK를 빌드해 AVD에 설치한다.

파일 역할만 빠르게 보려면 [기능별 파일 요약](file-guide.md)을 먼저 읽는다.

**1. 현재 구현된 범위**

Pleos AVD의 핸들·페달 값을 Kotlin에서 읽고 검사한 뒤 Unity가 JSON 스냅샷으로 가져온다. Unity에는 작은 주차장, 차량 프리팹, 전진·후진 미리보기, 3종 시점, 진단 HUD와 Figma 기반 메뉴 5개 화면이 있다. 정밀 차량 물리·충돌·주차 성공 판정은 앞으로 구현한다. 앱 진입은 `MainMenu.unity`이며 [UI 실행과 Scene 구성 안내](ui-and-scenes.md)에 화면별 범위를 정리했다.

연속 가속·브레이크 비율은 현재 AVD 콘솔의 HTTP 어댑터로 읽는다. 실차 SDK에서 연속 페달 비율을 확보한 상태를 뜻하지 않는다.

**2. 저장소의 세 부분**

| 위치 | 담당 | 결과 |
|---|---|---|
| `app/` | 기존 Kotlin 2D 입력·주차 검증 앱 | `com.example.pleosdemo` APK |
| `vehicle-bridge/` | 공용 차량 입력·검증·Unity JNI 연결 | Android AAR 라이브러리 |
| `unity/InputProbe/` | Unity 씬·차량·카메라·연습 흐름 | `com.example.pleosdemo.unityprobe` APK |

공용 Kotlin 입력 원본은 이제 `vehicle-bridge`에만 있다. 기존 앱은 `implementation(project(":vehicle-bridge"))`로 사용하고 Unity는 생성된 AAR를 포함한다. 파일을 `app`에서 복사해 컴파일하던 Sync 단계는 제거했다.

Android Studio는 저장소 루트, Unity Hub는 `unity/InputProbe` 폴더를 연다. 두 APK는 앱 ID가 달라 AVD에 나란히 설치할 수 있다. Unity APK 안에 Kotlin 라이브러리가 포함되므로 기존 2D 앱을 먼저 실행할 필요는 없다.

**3. 입력과 실행 흐름**

```text
Pleos SDK: 조향·브레이크 상태 ─┐
AAOS: 실제 기어·속도 ──────────┤→ PleosVehicleInput
AVD HTTP: 가속·브레이크 비율 ───→ AvdConsolePedals
                                  ↓
Kotlin UnityInputState: 검사·0~1 변환
                                  ↓
PleosUnityBridge.readSnapshot(): JSON → PleosInputSource
PC W·Space·좌우 방향키 ───────────────→ KeyboardInputSource
                                  ↓ 같은 InputSnapshot 계약
VehicleInputReceiver → InputSnapshotInterpreter → VehicleControlInput
                                  ↓
GameManager: 시작·정지 판단 → VehicleController: 이동·바퀴
                                  ↓
VehicleCamera / PracticeHud / InputDiagnostics
```

Kotlin의 `start`, `readSnapshot`, `stop`은 Unity C#이 JNI로 호출한다. 두 언어 사이에 별도의 네트워크 서버는 없다. HTTP는 AVD의 페달 공급자를 읽는 경로다.

실제 기어는 P(`4`), 실제 속도는 0을 유지한다. 게임 안의 `Virtual D/R`과 가상 속도는 별개다. 검증한 AVD에서 콘솔 조향 +25도는 SDK -25도로 반환된다. `UnityInputState`에서 부호를 반전해 Unity에는 +25도로 전달한다. SDK 원본과 PC 키보드 입력은 변경하지 않는다.

**4. Unity 폴더와 책임**

```text
Assets/_Parkside/
├─ Scripts/
│  ├─ Core/GameManager.cs
│  ├─ Input/
│  │  ├─ InputSnapshot.cs
│  │  ├─ VehicleControlInput.cs
│  │  ├─ InputSnapshotInterpreter.cs
│  │  ├─ VehicleInputReceiver.cs
│  │  └─ Sources/
│  │     ├─ VehicleInputSource.cs
│  │     ├─ KeyboardInputSource.cs
│  │     └─ PleosInputSource.cs
│  ├─ Vehicle/VehicleController.cs
│  ├─ Camera/VehicleCamera.cs
│  ├─ UI/PracticeHud.cs
│  └─ Diagnostics/InputDiagnostics.cs
├─ Scenes/ParkingPractice.unity
├─ Prefabs/Vehicles/PracticeCar.prefab
├─ Materials/
└─ Editor/
   ├─ Build/ParksideAndroidBuild.cs
   └─ Validation/
      ├─ PracticeSceneValidator.cs
      └─ PracticeProjectValidation.cs
```

| 코드 | 책임 | 담당하지 않는 것 |
|---|---|---|
| `GameManager` | 시작·일시정지·리셋·가상 D/R, 씬의 연습 범위 확인 | JSON 해석·오브젝트 생성 |
| `InputSnapshot` | Kotlin JSON 필드 정의, 스키마 v1 | 이동 계산 |
| `VehicleControlInput` | 차량이 사용할 유효한 조향·가속·제동 값 | SDK 조회 |
| `InputSnapshotInterpreter` | JSON 파싱, 스키마·입력 유효성·신선도 검사 | 화면·차량 조작 |
| `VehicleInputReceiver` | 선택한 공급자 폴링·재연결·검사 연결 | 키 처리·JNI 구현·이동 |
| `VehicleInputSource` | 입력 공급자의 공통 계약 | 차량 조작 |
| `KeyboardInputSource` | W·Space·좌우 방향키를 스냅샷으로 변환 | 차량 직접 이동 |
| `PleosInputSource` | Kotlin JNI 연결과 JSON 수신 | 키보드 입력 |
| `VehicleController` | 조향 바퀴, 가속·제동·이동, 전진·후진 | 실제 차량 상태 읽기·UI |
| `VehicleCamera` | 씬에 지정한 앵커로 추적·탑뷰·운전자 시점 전환 | 차량 이동 |
| `PracticeHud` | Canvas 텍스트·상태·버튼 활성 여부 갱신 | UI 생성·배치·직접 입력 수신 |
| `InputDiagnostics` | FPS와 검증용 로그 출력 | 게임 진행 판단 |

`GameManager`는 씬에 하나만 두고 Inspector 참조로 연결한다. 싱글턴·전역 검색을 사용하지 않는다. 입력 수신기가 먼저 갱신되고, GameManager가 검증된 입력을 Controller의 `Simulate()`에 넘긴다. 카메라는 `LateUpdate()`에서 따라간다.

**5. 맵과 차량은 씬·프리팹에서 편집한다.**

현재 진입 씬은 `Assets/_Parkside/Scenes/MainMenu.unity`다. 연습장 자체만 편집·시험할 때는 `ParkingPractice.unity`를 직접 연다. 기존 `InputProbe.unity`와 `PleosInputProbe.cs`는 제거했다.

```text
ParkingPractice
├─ Environment          # 바닥·주차선·목표 주차면
├─ Sun                  # 조명
├─ PracticeCar          # 차량 프리팹 인스턴스
│  ├─ Body / Cabin / FrontMarker
│  ├─ FrontLeft / FrontRight / RearLeft / RearRight
│  └─ CameraAnchors
│     ├─ Chase
│     ├─ Top
│     └─ Driver
├─ SpawnPoints/PracticeSpawn
├─ MainCamera
├─ Systems
│  ├─ InputReceiver
│  ├─ GameManager
│  └─ InputDiagnostics
├─ PracticeCanvas       # Text·Button·Image와 PracticeHud
└─ EventSystem          # 마우스·터치 UI 입력
```

Play 전에도 맵과 Canvas UI 배치가 보인다. UI의 위치·폰트·색상·문구와 버튼 On Click 연결은 Scene의 Canvas에서 편집한다. `PracticeHud`는 저장된 UI 참조의 값만 갱신하며 OnGUI나 런타임 UI 생성은 사용하지 않는다. 런타임 코드가 맵·차량·조명·카메라를 생성하지 않는다. 빌드는 Build Settings에 등록한 저장된 씬을 읽고 패키징하며, 씬을 다시 만들거나 저장하지 않는다.

맵 담당자는 `Environment`를 편집하고, 차량 모양과 바퀴·카메라 앵커는 `PracticeCar.prefab`에서 편집한다. 차량 전방은 로컬 +Z, 월드 단위는 1유닛=1m로 맞춘다. 차량 모델을 바꿀 때 `VehicleController`의 앞바퀴 참조와 MainCamera의 앵커 참조를 확인한다. 새 차량 인스턴스로 교체했다면 GameManager·HUD·Diagnostics 참조도 다시 연결한다.

스폰 위치는 `PracticeSpawn`, 연습 범위는 GameManager의 `Practice Bounds`에서 조정한다. 범위를 벗어나면 해당 스폰으로 리셋된다. 재질은 `Materials`의 자산으로 공유한다.

**6. Kotlin 주요 파일**

원본 위치는 `vehicle-bridge/src/main/java/com/example/pleosdemo/`다.

| 파일 | 역할 |
|---|---|
| `VehicleInput.kt` | 값·앱 수신 시각, 실제 P·정차·SDK 신호 검사 |
| `PleosVehicleInput.kt` | Pleos SDK 구독과 AAOS 프로퍼티 조회·해제 |
| `AvdPedals.kt` | 페달 비율·신선도 검사 |
| `AvdConsolePedals.kt` | 별도 스레드에서 AVD HTTP 페달 조회 |
| `unity/UnityInputState.kt` | 공용 입력을 Unity 계약으로 변환 |
| `unity/PleosUnityBridge.kt` | Android 메인 스레드에서 수신 시작·종료, 최신 JSON 반환 |

신호가 없으면 0으로 위장하지 않고 누락 상태와 사유를 유지한다. SDK 신호는 앱 수신 후 3초 이내, 페달은 600ms 이내여야 한다. Unity도 JSON 수신 후 650ms 이내인지 확인한다. 입력 사용 불가 시 가속 0·가상 제동 1로 차단하며, 실제 차량에 제동 명령을 보내지 않는다.

JSON 필드 이름·타입을 바꾸면 Kotlin의 직렬화 코드와 C#의 `InputSnapshot`, 입력 해석 검사도 함께 변경한다. 연속 페달 비율은 0~100, Controller용 가속·제동은 0~1이다.

**7. PC에서 작업하는 순서**

1. Unity Hub Projects에 `unity/InputProbe` 폴더를 추가한다.
2. Unity 2022.3.16f1로 연다. PC 편집·키보드 미리보기는 Windows 대상으로 가능하며 Android 모듈이 없어도 컴파일된다.
3. `Assets/_Parkside/Scenes/ParkingPractice.unity`를 연다.
4. Scene/Hierarchy에서 편집하고 저장한다.
5. Play → `Start preview`를 누른다. W로 가속, Space로 제동, 좌우 방향키로 조향한다. A/D·위아래 방향키로 차량을 직접 이동시키지 않는다.
6. Inspector 값은 Play를 정지한 뒤 편집·저장한다. Play 중 변경은 정지하면 되돌아갈 수 있다.
7. `PARKSIDE → Validate scene and input`을 실행해 참조·입력·Controller 검사를 확인한다.

현재 HUD는 씬에 저장된 uGUI Canvas다. 이벤트는 버튼의 Inspector에 연결되어 있고, Space와 방향키가 UI 확인·탐색으로 중복 처리되지 않도록 EventSystem 키보드 탐색을 끄고 버튼 Navigation을 None으로 두었다. 이동은 Transform 기반 저속 미리보기이며 최대 2m/s, 브레이크가 0.01을 넘으면 즉시 정지한다. 정밀 제동·타이어·서스펜션·충돌 물리는 미구현이다. 해당 개발은 VehicleController 중심으로 진행한다.

**8. Android 빌드와 AVD 실행**

APK를 만들 PC에는 Android Build Support, SDK/NDK/OpenJDK, Android SDK 플랫폼 34·37, Build Tools 34.0.0, Pleos AVD, Python 3.9 이상을 준비한다. Hub 로그인·라이선스 활성화가 필요하다.

검증 PC에는 Editor가 두 개 있다. Android 모듈이 설치된 실행 파일은 다음이다.

```text
C:\Program Files\Unity\Hub\Editor\2022.3.16f1\Editor\Unity.exe
```

`2022.3.16f1-x86_64` 폴더의 Editor는 Android 모듈이 없고 PC 편집용으로 사용할 수 있다. Hub에서 버전뿐 아니라 경로도 확인한다.

아래는 검증 PC의 경로 예시다. 팀원 PC에서는 자신의 경로로 바꾸고 `local.properties`도 맞춘다. 배치 빌드 전 같은 Unity 프로젝트를 Editor에서 닫는다.

빌드·콘솔 스크립트는 `tools/android-environment.ps1`로 SDK·JDK·Unity 경로를 탐색한다. 기본 설치가 아니면 `build-unity-input-probe.ps1 -UnityEditor <Unity.exe> -AndroidSdk <SDK> -JavaHome <JDK 25>`로 지정한다. 아래 환경 변수 예시는 수동 Gradle 실행에도 사용할 수 있다.

```powershell
Set-Location 'D:\Android\Projects\PleosDemo'
$env:ANDROID_HOME = 'D:\Android\Sdk'
$env:JAVA_HOME = 'D:\Android\AndroidStudio\jbr'
& .\tools\build-unity-input-probe.ps1
```

출력은 `build/unity-probe/parkside-input-probe.apk`다. Kotlin 라이브러리 → Unity AAR 복사 → 저장된 씬 내보내기 → APK 패키징 순서다. C#·씬만 바뀌었더라도 APK를 다시 만들어야 AVD에 반영된다.

Pleos AVD를 Cold Boot하고 P·속도 0에서 자동 검사한다.

```powershell
py -3 .\tools\validate-unity-input-probe.py
```

기본 대상은 `emulator-5554`이며 `--serial`, `--adb`로 바꿀 수 있다. 결과는 `build/unity-probe/validation.json`과 로그·PNG에 저장된다. 다른 콘솔 세션이 살아 있으면 연결 종료 검사를 건너뛴 사실도 기록한다.

수동 조작은 다음 명령을 실행하고 터미널을 유지한다.

```powershell
& .\tools\start-parking-console.ps1
```

PC 브라우저의 `http://127.0.0.1:8090/`을 열고 AVD의 `Pleos Unity Input Probe` 앱에서 시작한다. 콘솔의 실제 기어는 P를 유지하고 가상 전진·후진은 앱에서 바꾼다. 자동 검사 종료 뒤 콘솔 세션이 닫혀 앱이 대기하는 것은 정상이다.

PC의 8090 프록시는 기본 콘솔의 P단 악셀 제한을 시험용으로 바꾼 화면을 전달한다. `adb forward tcp:8088 tcp:8088`로 AVD 서버와 연결한다. APK는 AVD 내부의 `127.0.0.1:8088/vehicle/state`를 직접 읽는다.

**9. 도구 버전과 검사**

| 구분 | 고정한 조합 |
|---|---|
| Unity | 2022.3.16f1, Built-in, GLES3, x86_64, IL2CPP |
| Kotlin 라이브러리 빌드 | Gradle 9.6.0 / AGP 9.4.1, 검증 PC JBR 25.0.3 |
| Unity APK 패키징 | 내장 Gradle 7.2 / AGP 7.1.2 / OpenJDK 11 / NDK r23b |
| Android SDK | 공용 Kotlin compile 37, Unity compile/target 34, Build Tools 34.0.0 |
| Pleos | Vehicle SDK 2.3.2, Connect v2.1.0 / Android 14 / x86_64 AVD |

Kotlin 단위 테스트는 입력 라이브러리 21개·기존 2D 앱 13개, 총 34개다. Unity의 `Validate scene and input`은 씬 참조·JSON·잘못된 입력 차단·전진·후진·제동·리셋을 검사한다. Editor 검사와 APK의 실제 SDK 입력 검사는 별도다.

`PracticeSceneValidator`는 저장된 씬 연결을 검사하며 빌드에서도 사용한다. `PracticeProjectValidation`은 여기에 입력·차량 회귀 검사를 더한다. 연습 컴포넌트가 없는 메뉴 씬은 차량·HUD가 없어도 된다. 공급자는 비활성화·삭제 시 연결을 정리하며, 새 공급자도 `Disconnect()`를 여러 번 호출해도 안전하게 구현한다. 진단 로그의 `allowed`는 Kotlin 원본, `inputReady`는 C# 검사까지 마친 결과다.

기존 AVD 입력 검사에서는 가속 37%, 콘솔 +25도에 대한 SDK -25도, 브레이크 50%, 입력 해제·연결 종료 시 차단을 확인했다. 최신 실행 결과는 [입력 검증 기록](unity-input-probe.md)에 기록한다. 최종 월드의 성능과 장시간 안정성, 실차 지원은 별도 검증 대상이다.

**10. 협업 규칙**

Unity 경험자는 입력·Controller·GameManager·카메라와 APK 통합을, 입문자는 맵·모델·재질·프리팹·UI 배치를 우선 맡는 구성이 적절하다. 같은 씬 파일을 동시에 수정하지 않도록 담당을 정하고, 맵이 커지면 별도 맵 씬·프리팹으로 나눈다.

Git에는 Kotlin 소스·테스트, Unity `Assets/Packages/ProjectSettings`, 모든 `.meta`, Gradle 설정·Wrapper, `tools`, `docs`를 공유한다. `.meta`에는 자산 참조 ID가 있으므로 함께 이동·커밋한다. 현재 Unity는 Visible Meta Files / Force Text로 설정되어 있다.

`Library/Temp/Logs/UserSettings`, `build`, 생성 AAR·APK·SDK 사본, `local.properties`는 공유하지 않는다. 생성된 `android-export`를 직접 고치면 다음 빌드에 덮일 수 있으므로 수정은 원본 Gradle 템플릿에 반영한다. 현재 작업은 로컬 변경이며 이 정리에서 커밋·푸시는 수행하지 않았다.

**11. 문제를 만났을 때**

| 증상 | 확인할 곳 |
|---|---|
| 예전 씬이 사라졌거나 화면이 비어 있음 | 새 `ParkingPractice.unity`를 열기 |
| 차량이 움직이지 않음 | Start preview, Game 창 키보드 포커스, InputReady·차단 사유 |
| APK에서 입력 대기 | 8090 콘솔 세션, P·속도 0, 권한, 샘플 경과 시간 |
| P에서 악셀 0 | 브라우저가 8090 프록시를 사용하는지 |
| AndroidPlayer 미지원 | PC는 Windows 대상으로 열기, APK는 모듈이 설치된 Editor 사용 |
| Safe Mode 컴파일 오류 | 최신 소스 적용 후 Ctrl+R, Console의 첫 컴파일 오류 확인 |
| 씬 검사 실패 | Inspector에서 누락된 컴포넌트·앞바퀴·스폰·카메라 참조 확인 |
| 빌드 뒤 콘솔 연결 끊김 | ADB 대상 확인 후 콘솔 시작 스크립트 재실행 |

자세한 빌드 재현 절차는 [Unity 입력 테스트 안내](unity-input-probe.md), 기존 2D 앱은 [README](../README.md)를 참고한다.
**입력 공급자 교체 방법**

`Systems/InputReceiver`의 `VehicleInputReceiver` 컴포넌트에서 `Desktop Source`와 `Android Source`를 지정한다. 기본 연결은 Desktop=KeyboardInputSource, Android=PleosInputSource다. Editor에서는 빌드 타깃과 관계없이 Desktop을 사용하고, Android APK에서만 Android 필드를 사용한다.

두 공급자는 모두 `VehicleInputSource`를 상속하며 `Connect`, `ReadSnapshot`, `Disconnect`를 구현한다. `ReadSnapshot`은 같은 `InputSnapshot`을 반환한다. 다른 입력 장치를 붙일 때 이 계약을 구현한 컴포넌트를 만든 뒤 해당 필드에 끼우면 된다. GameManager·Interpreter·VehicleController는 수정하지 않는다.

실행 중 공급자 참조를 바꾸거나 비활성화하면 현재 연습을 일시정지한다. 새 공급자가 정상이어도 자동으로 주행을 재개하지 않는다. 연결이 실패했을 때 키보드로 자동 대체하지 않는다.

KeyboardInputSource의 기본 키는 W=가속, Space=브레이크, Left/Right Arrow=조향이다. 키는 컴포넌트 Inspector에서 바꿀 수 있다. W와 Space를 동시에 누르면 제동이 우선한다. 가상 후진은 UI의 Virtual D/R로 고른다.

**Canvas 편집 위치**

`PracticeCanvas/DebugPanel` 아래의 Text·Image·Button을 Scene에서 편집한다. 위치·크기·폰트·색상·고정 문구는 각 컴포넌트의 Inspector 값이다. `PracticeHud`는 런타임 수치·상태만 갱신한다. 버튼의 On Click은 GameManager 또는 VehicleCamera의 공개 메서드로 씬에 연결되어 있다.

`EventSystem.Send Navigation Events=false`, 버튼 Navigation=None은 Space 제동·좌우 조향이 UI 버튼 제출·탐색과 충돌하지 않기 위한 설정이다. UI를 바꿀 때도 이 키를 차량 입력과 중복 사용하지 않는다. uGUI 1.0.0 패키지는 Packages 목록과 함께 공유한다.

**12. 부산 드라이브 슬라이스 (2026-11-08 추가, 2026-11-09 v2)**

공모전 데모용 주행 씬은 `Assets/_Parkside/Scenes/BusanDrive.unity`다. 마린시티1로 서쪽 끝 출발 → 해운대해변로 → 광안대교 현수 구간 → 광안해변로 북상의 약 10km 단일 코스이며, OSM 기반 실좌표(1:1 미터)를 사용한다. 코스 450m 안의 실제 도로망(곁길)과 건물 풋프린트가 함께 생성된다. 교량 높이·주탑·추정 건물 높이는 설계값이고 실측이 아니다. 낮 조명이다.

- **빌드 목록에는 `enabled: 0`으로 들어 있다.** `Assets/BusanEnvironment`는 git에 추적되지 않으므로 활성 상태로 목록만 공유되면 Android 빌드 검증기가 멈춘다. 빌더도 항상 비활성으로 등록한다. PC 빌드에 넣으려면 팀 합의 후 직접 켠다. 검증은 `PracticeSceneValidator.ValidateScene`을 직접 호출한다(빌더가 끝에 실행한다).
- 데이터 재생성 순서(저장소 루트): `py -3 tools/fetch-busan-slice-landcover.py`(캐시 없을 때만) → `py -3 tools/prepare-busan-slice.py`(코스·도로망·지면·건물 JSON과 평면도 PNG) → `py -3 tools/make-busan-slice-textures.py`(파사드 크롭·간판 아틀라스).
- 씬 재생성: Unity 메뉴 `Busan/Build drive slice scene`. 매번 `ParkingPractice.unity`를 파일 복사로 덮어쓴(GUID 유지) 뒤 전체를 다시 배선한다. 메시는 범주별 컨테이너 asset(`GeneratedSlice/Meshes`)이다.
- 조작(v3): 부산 씬만 `DriveKeyboardSource`를 쓴다(주차 씬의 `KeyboardInputSource` W/Space 규약은 그대로). W/↑ 가속, S/↓ 제동 겸 후진, Space 제동만, ←→/A/D 조향. 정지 상태에서 S를 새로 누르면 즉시 후진, 눌러서 멈춘 뒤 계속 누르면 0.8초 후 후진, 후진 중 S는 약 11km/h, W는 제동이며 정지 후 같은 규칙으로 전진 복귀. 조작 안내 팝업은 기본 숨김(G로 표시). 이전 기록: W 가속, Space 제동, ←→ 조향, V 시점, **R 코스 복귀(막다른 길·범위 이탈 탈출구)**, T 자동주행, 1~5 점프, G 안내. PC 런타임에서만 `DriveAssist`가 그림자를 켜고 종료 시 복원한다(전역 품질값이라 주차 씬에 새면 안 된다). 모바일은 후처리를 끈다.
- `VehicleController` 확장 필드(brakeDeceleration, highSpeedSteeringAngle, maxLateralAcceleration, steeringResponse, groundFollow, groundMask, SpeedCap)는 **기본값이 모두 "끔"이라 기존 주차 동작과 같다.** BusanDrive 씬의 차량 인스턴스에만 오버라이드가 걸려 있다. `groundFollow`는 현재 높이에 가장 가까운 면(±1.2m)만 지면으로 보고, 도로 끝에서는 ±10~50도 방향으로 미끄러진다. 도로 콜라이더는 레이어 30이다.
- 메뉴 연결은 아직 없다. 부산 카드를 `ParksideMenu`에 붙일 때 주의: `PARKSIDE/Author Figma menu scene`을 실행하면 **EditorBuildSettings 씬 목록을 MainMenu·ParkingPractice 두 개로 덮어쓴다.** 부산 씬을 포함하려면 실행 후 다시 추가한다.
- **합치는 순서(2026-11-10):**
  1. 부산 쪽 커밋에 `Assets/BusanEnvironment`(메시 포함 약 198MB, 100MB 넘는 단일 파일 없음), `Assets/BusanEnvironment.meta`, `Assets/_Parkside/Scenes/BusanDrive.unity`와 `.meta`, `tools/`, `docs/`를 모두 넣는다. 씬만 올라가면 메시 참조가 비어 분홍색 또는 빈 씬이 된다.
  2. 충돌이 날 수 있는 추적 파일은 셋이다: `ProjectSettings/EditorBuildSettings.asset`(씬 목록 3줄 추가), `Assets/_Parkside/Scripts/Vehicle/VehicleController.cs`(기본값이 전부 꺼진 필드 추가), `docs/team-handoff.md`. 제주 씬도 목록에 들어가므로 양쪽 씬 줄을 모두 남긴다.
  3. 합친 PC에서 `PARKSIDE → Validate scene and input`을 돌린 뒤 빌드 목록의 BusanDrive를 켠다. 켜면 Android 빌드 검증이 이 씬도 검사한다.
  4. 메뉴 카드는 `SceneManager.LoadSceneAsync("BusanDrive")`로 연다. 빌드 목록에서 꺼져 있으면 런타임에 씬을 찾지 못한다.
  5. 도로 레이어는 30번이다. 제주 맵이 같은 번호를 쓰면 씬이 달라 충돌은 없지만 `groundMask`는 각 씬 차량에서 확인한다.
- **PC 실행 파일 확인 결과(2026-11-10):** Windows 빌드 241MB, 빌드 오류·경고 0건, 실행 로그에 스크립트·셰이더 오류 없음, Intel Iris Xe 1280x720 창에서 30fps(상한 30). **Android APK는 아직 한 번도 빌드하지 않았다.** 부산 담당 PC에는 Android 모듈이 없다. 정적 삼각형 69만 개에 교통 106만 개, 주차 193만 개라 AVD에서는 그대로 돌리기 무겁다고 봐야 하고, 측정값은 없다. 또 Android 입력에는 후진이 없고 키보드의 R(코스 복귀)도 없어서, 도로 끝에 닿으면 빠져나올 수단이 없다.
- 창이 포커스를 잃거나 입력이 끊겨 GameManager가 멈추면 `DriveAssist`가 둘 다 돌아왔을 때 다시 시작한다. H로 패널을 띄워 `Pause`를 누른 경우에는 멈춘 채로 있다.
- 도로망(v3): `tools/prepare-busan-slice.py`가 서비스 도로(골목, 폭 4~6m, 건물 벽 여유 2.2m 이상만), 코스 옆 나란한 도로 처리, 합성 끝점(`net.mouths`), 간선 폐쇄 표시(`net.closures`: 주황-흰 PE 방호벽과 러버콘)를 만든다. 검증은 `py -3 tools/check-busan-network.py`(종료 코드 0)와 Unity `sweep`의 `REACH roads=a/b`. 코스(9839.5m, 점프 s)는 바뀌지 않는다.
- 차량(v3): 빌드 때 `GeneratedSlice/TrafficCars/*.prefab`에 차종별 구운 프리팹(메시 1개, 정점색, `CarBaked` 셰이더, LODGroup 2%)을 만든다. 원본 prefab, 메시, 재질은 읽기만 한다. 교통은 `RouteTraffic`(풀 34, 교차로 없는 차 14대), 주차는 최대 70대.
- **도로망(v4, 2026-11-10): SUMO netconvert로 만든다.** 교차로 면(모서리 곡선), 차로, 인도와 연석, 횡단보도, 정지선, 차선 도색, 신호 위치와 신호 주기가 여기서 나온다. 고가 코스(광안대교와 진입 고가)만 예전 리본으로 남는다.
  - 다시 만드는 순서(저장소 루트): `py -3 tools/prepare-busan-slice.py` → SUMO 환경의 Python으로 `tools/build-busan-sumo-net.py` → 같은 Python으로 `tools/export-busan-sumo-geometry.py` → Unity `Busan/Build drive slice scene`. 결과 `Editor/Data/BusanSumoGeometry.json`이 있으면 빌더가 SUMO 도로를 쓰고, 없으면 예전 도로를 쓴다.
  - SUMO 환경: `py -3 -m venv <폴더>` 후 `pip install eclipse-sumo==1.28.0 sumolib==1.28.0 shapely==2.2.0 mapbox-earcut==2.1.0`. **netconvert는 설치 경로에 한글이 있으면 "unknown error"로 멈춘다.** 패키지의 `sumo` 폴더를 영문 경로(예: `C:\c\sumo-1.28`)에 복사하고 `BUSAN_SUMO_HOME`을 그 경로로 지정한다. SUMO는 EPL-2.0이며 생성물만 저장소에 들어간다.
  - 신호: `TrafficSignals`가 SUMO 고정 주기 신호를 돌리고 램프는 셰이더 하나(`SignalLamp`)로 켠다. 다른 차는 빨간불과 먼 노란불에 정지선 앞에서 멈춘다. 내 차는 강제로 세우지 않는다.
  - 건물: 파사드는 대지에서 가장 높은 지면부터 시작하고 낮은 쪽은 콘크리트 기단이다. 맵 가장자리 빈 땅에는 추정 건물(실제 건물 아님)을 채운다.
  - 프리팹: 생성 결과는 범주별 프리팹(`GeneratedSlice/Prefabs/*.prefab`)으로 저장되고 씬에는 그 인스턴스가 들어간다. 다시 빌드하면 덮어쓰므로 손으로 고친 내용은 빌더나 데이터에 반영해야 남는다.
  - 재질: 인도 보도블록과 기단 콘크리트는 Poly Haven CC0 텍스처(`GeneratedSlice/tex/ph`, 출처 `CREDITS.txt`)다.
  - **v5(2026-11-10 새벽), 실제 거리 모습 반영.** 기준은 카카오 로드뷰 관찰이며 사진은 저장하지 않았다. 기준표는 `부산맵_화면확인/v5_맵점검/실제모습_기준표.md`에 있다.
    - 주택가 이면도로: 보도가 없고 중앙선이 없다. 가장자리에 노란 실선이 있고, 아스팔트가 건물 앞까지 8 m 이어진다(`Apron`, 주행면 아님).
    - 큰길: 보도블록이 상가 앞까지 6 m 이어진다(`Frontage`). 포장 바깥 가장자리는 1.6 m 경사로 떨어진다.
    - 골목 빌라 줄짓기: 코스 700 m 안의 이면도로를 따라 추정 빌라(실제 건물 아님)를 세우고, 45%는 1층 필로티 주차장이다(`BusanSliceFiller.AddStreetRows`).
    - 거리 시설: 횡단보도 끝에 노란 점자블록과 볼라드, 큰길 가로수 9~12 m 간격과 보호틀, 맨홀과 보수 패치, 건물 밑동 어둡게(그림자 꺼진 안드로이드용).
    - 차도에 0.8 m 넘게 들어온 OSM 건물은 짓지 않는다(11동).
    - 내보내기는 `BusanSliceGround.json`도 읽는다(바다·모래사장 위에 포장하지 않음). `prepare-busan-slice.py`를 다시 돌렸다면 내보내기도 다시 돌린다.
  - **주행 점검 봇:** 브리지 명령 `play <초> <폴더> courserun [warp=<n>] [from=<m>]`은 플레이어 컨트롤러로 코스를 끝까지 운전한다. 범프, 피치 점프, 도로 이탈, 막힘, 차로 이탈, 교통 대기 시간을 보고한다. 맵을 고치면 이것과 92곳 카메라 비교를 함께 돌린다.
  - 신호 주기: 녹색 14초, 황색 3초, 전적색 2초 이하이고 주기는 64초 이하다. 단계가 많은 신호 2개는 126초까지 남는다. 빨간불에 서 있는 차 뒤에서는 내 차가 통과하지 않고 기다린다(70초가 넘으면 통과).
- 차량 앞뒤 규칙: 구운 차는 전부 로컬 +Z가 앞이다(교통과 주차 모두 +Z 방향으로 놓는다). 원본 모델의 앞이 -Z면 `BusanSliceVehicles.cs`의 `CarSpec.noseAtMinusZ`를 켠다(현재 Tucson, i30, Azera, Ioniq5). 부품 이름에 전조등 표시가 없는 모델이 있어 Unity 메뉴가 아닌 눈으로 확인해야 한다. 새 차종을 넣으면 앞과 뒤에서 한 번씩 찍어 본다.
- **v3에서 새로 쓰는 차량 모델 출처(전부 독립 제작자의 3D 모델이며 Hyundai 공식 자산이 아니다, 변경 사항: 여러 부품을 한 메시로 합치고 재질 값을 정점색으로 구움, 유리는 불투명 어두운 값으로 교체, 도장 부분만 색 교체, Ioniq 5는 제작자 이름이 적힌 번호판 글자 부품을 빼고 한국식 번호판으로 교체)**
  - Hyundai Porter II: **max7th**, https://www.thingiverse.com/thing:4685394 , CC BY 4.0 (https://creativecommons.org/licenses/by/4.0/). 원 STL 부품을 조립, 재질 분리, 타이어 삼각형 축소한 파생(`CloudPilot/ParkingContext20261105/PorterData/CREDITS.txt`). 교통 최대 2대.
  - Hyundai Ioniq 5 2022: 원작 **andikapratamaw** (https://sketchfab.com/3d-models/hyundai-ioniq-5-lowpoly-675e78311e8440d88714bd212cb7a8fb , CC BY 4.0), OpenX 변환 **Dogan Ulus / VevaLabs** (MPL-2.0 AND CC-BY-4.0, https://github.com/vevalabs/openx-assets/releases/tag/20261015). `ParkingLotEnvironment/CREDITS.md` 참고.
  - Hyundai Azera 2009 (저폴리 LOD1 형상): **Nieve5677**, https://sketchfab.com/3d-models/711b67ea05f9441a863dce4badaca660 , CC BY 4.0. 택시는 같은 Azera 메시에 절차 생성한 지붕 표시등(로고, 실제 상호 없음)을 더한 것.
  - 이미 쓰던 모델의 LOD1 형상도 같은 출처다: Hyundai Tucson 2015 **razor24**, Hyundai i30 2011 **D3NK**, 박스트럭 LCT 3000 '95 **Daniel Zhabotinsky**(전부 CC BY 4.0). 전체 표기는 `ParkingLotEnvironment/VEHICLE-SOURCES.md`, `CREDITS.md`.
