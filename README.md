# 파킹 트래블 / PleosDemo

## 공모전 제출 정보

| 항목 | 내용 |
|---|---|
| 팀명 | 잠자는 바보들 |
| 앱 이름 | 파킹 트래블 |
| 한 줄 소개 | 정차·주차 중 실제 핸들과 브레이크로 국내외 도시 도로를 달려 보는 차량용 운전 연습 앱 |
| APK 위치 | `apk/잠자는바보들_파킹트래블_release.apk` (release 빌드) |
| 테스트 환경 | Windows 11 PC, Unity 2022.3.16f1(Windows 64-bit), Pleos AVD(`emulator-5554`) |

**실행 방법**

- PC: 아래 [PC에서 시작](#pc에서-시작)대로 Unity Hub에서 `unity/InputProbe`를 열고 씬을 Play한다.
- 주행 씬: [부산 명소 순환](docs/busan-drive.md) · [제주 해안](docs/jeju-drive.md) · [파리 에펠탑](docs/paris-drive.md)
- 차량(Pleos AVD): 위 APK를 설치하거나 아래 [Pleos AVD에서 시작](#pleos-avd에서-시작)대로 빌드한다. 자동 검사는 P단·속도 0 상태에서 진행한다.

Pleos 입력으로 조작하는 Unity 주차 시뮬레이터의 개발 시작점이다. 현재 씬에는 테스트 맵·차량·Canvas UI가 있으며, 저속 이동과 입력 연결을 확인할 수 있다. 정밀 차량 물리·충돌·주차 판정은 다음 개발 범위다.

## PC에서 시작

1. Unity Hub의 **Add → Add project from disk**에서 `unity/InputProbe` 폴더를 등록한다.
2. Unity **2022.3.16f1**, PC 작업은 **Windows 64-bit** 대상으로 연다.
3. `Assets/_Parkside/Scenes/MainMenu.unity`를 열고 Play한다.
4. **연습 → 후진 주차 선택 → 연습장 열기 → Start preview** 순서로 진행한 뒤 **W 가속 / Space 제동 / ←·→ 조향**으로 확인한다.
5. `PARKSIDE → Validate scene and input`으로 씬 연결·입력·차량 동작을 검사한다.

맵은 씬, 차량은 `PracticeCar.prefab`, UI는 `PracticeCanvas`에서 편집한다. PC에서는 키보드, Android APK에서는 Pleos 공급자를 사용한다. `Systems/InputReceiver`의 Desktop Source / Android Source 참조로 공급자를 교체한다.

Figma 기반 메뉴 5개 화면은 `MainMenu`의 `MenuCanvas`에서 편집한다. 맵 검색·필터, 시점 선택, 기존 연습장 진입·홈 복귀가 연결되어 있다. 해외 주행·멀티·점수 판정은 준비 중이다. [UI 실행과 Scene 구성 안내](docs/ui-and-scenes.md)를 참고한다.

실사 재질과 조명을 확인하는 별도 Scene은 `Assets/_Parkside/Scenes/ParkingLookdev.unity`다. Play 후 1~4로 시점을 바꾸며 볼 수 있다. [작은 주차장 시각 검증 안내](docs/parking-lookdev.md)에 조작과 에셋 출처를 정리했다.

제주 주행 시안은 별도 Scene `Assets/_Parkside/Scenes/JejuCoastalDrive.unity`다. 예래·중문 해안, 월평·강정 연결도로, 1100고지 부근을 실제 도로·고도로 구성했으며, 플레이어 주변에 각자 목적지를 가진 차량이 다닌다. [제주 Scene 실행·조작 안내](docs/jeju-drive.md)를 참고한다.
- [파리 에펠탑 주행 씬](docs/paris-drive.md)
- [부산 명소 순환 주행 씬](docs/busan-drive.md): `Assets/BusanEnvironment/Landmarks/BusanLandmarks.unity`. 해운대에서 출발해 광안대교 또는 광안리 해안을 지나 한 바퀴 도는 코스이며, 움직이는 차와 신호가 있다.

## Pleos AVD에서 시작

Windows PowerShell 7 기준이다. APK 빌드에는 Unity Android Build Support·SDK/NDK/OpenJDK, Android SDK 플랫폼 34·37 및 Build Tools 34.0.0, 루트 Gradle용 JDK 25가 필요하다. 자동 검사와 PC 콘솔에는 Python 3.9 이상을 사용한다. 같은 프로젝트를 Unity Editor에서 닫고 저장소 루트에서 실행한다.

```powershell
.\tools\build-unity-input-probe.ps1
py -3 .\tools\validate-unity-input-probe.py
```

빌드 결과는 `build/unity-probe/parkside-input-probe.apk`다. 자동 검사는 실행 중인 Pleos AVD(`emulator-5554`)가 P단·속도 0인 상태에서 진행한다. 수동 조작은 `tools/start-parking-console.ps1`을 실행하고 PC에서 `http://127.0.0.1:8090/`을 연다.

도구는 Unity 기본 설치 경로, `ANDROID_HOME` 또는 `local.properties`의 `sdk.dir`, Android Studio JBR 등을 탐색한다. 다른 경로는 `-UnityEditor`, `-AndroidSdk`, `-JavaHome`으로 지정한다. 루트 Kotlin 빌드의 JDK 25와 Unity APK 패키징의 내장 JDK 11을 구분한다. 자세한 설치·경로·실행 순서는 [빌드·AVD 검사 안내](docs/unity-input-probe.md)를 따른다.

## 구조와 팀원 안내

| 위치 | 역할 |
|---|---|
| `unity/InputProbe/` | 현재 Unity 앱: 씬·차량·카메라·게임 진행·UI |
| `vehicle-bridge/` | Pleos 입력 수신·검사·Unity JNI 연결 Kotlin 라이브러리 |
| `app/` | 기존 Kotlin 2D 검증 앱 |
| `tools/` | APK 빌드·PC 콘솔·AVD 검사 |
| `docs/` | 파일 역할·작업 순서·검사 기록 |

- [기능별 파일 요약](docs/file-guide.md): 파일별 역할과 읽는 순서
- [팀원 인수인계](docs/team-handoff.md): 입력 흐름·Unity 편집·협업 방식
- [빌드·AVD 검사 안내](docs/unity-input-probe.md): 도구 조합·명령·검사 범위
- [기존 2D 앱 안내](docs/legacy-2d.md): 이전 프로토타입 실행·구현

Git에는 원본과 Unity `.meta`를 함께 공유한다. `Library`, 로컬 설정, 생성 AAR·APK, 빌드 결과는 제외한다. 입력 라이브러리는 빌드 스크립트가 다시 만든다. 연속 페달 비율은 현재 AVD 전용 HTTP 입력이며, 실제 차량 지원은 별도 검증이 필요하다.
