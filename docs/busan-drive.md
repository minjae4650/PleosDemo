# 부산 명소 순환 주행

Unity **2022.3.16f1**에서 `Assets/BusanEnvironment/Landmarks/BusanLandmarks.unity`를 열고 Play한다. 해운대에서 출발해 한 바퀴 도는 순환 코스이며, 광안대교를 타면 서면, 타지 않으면 광안리 해안으로 간다. 실제 부산을 줄여 이은 주행용 코스이며 실측 지도는 아니다.

## 코스

| 경로 | 길이 | 지나는 곳 |
|---|---|---|
| 광안리 해안 | 약 3.6 km | 해운대 → 갈림길 → 광안리 해변 도로 → 서면 쪽 대로 → 터널 → 해운대 |
| 광안대교 | 약 4.4 km | 해운대 → 갈림길 → 광안대교 → 서면 → 터널 → 해운대 |

- 가운데 산은 숲이고, 산을 지나는 터널로 순환이 이어진다.
- 움직이는 차 92대가 신호를 지키며 달린다. 다리 양 끝에서는 반대 방향 차가 지하차도로 지나간다.
- 플레이어 차로의 앞차와는 거리를 두며, 지금은 차를 뚫고 지나갈 수 있다(충돌 판정은 아직 없음).
- 도로 끝에 닿으면 차가 가장자리를 따라 돌아간다.

## 조작

| 키 | 동작 |
|---|---|
| W / ↑ | 가속 |
| S / ↓ | 제동, 멈춘 뒤 누르면 후진 |
| A·D / ←·→ | 조향 |
| Space | 제동 |
| V | 시점 바꾸기(기본은 운전석) |
| F | 갈림길에서 탈 경로 바꾸기(광안리 해안 / 광안대교) |
| T | 자동 주행 켜기·끄기 |
| R | 코스로 돌아가기 |
| G | 안내 표시 켜기·끄기 |
| H | 계기판 표시 켜기·끄기 |
| 1–5 | 해운대 / 광안리 / 광안대교 / 서면 / 터널로 이동 |

## 성능과 테스트 환경

- Windows 11 PC, Unity 에디터 Game 창(1635×705)에서 평균 약 51~54 fps를 확인했다. 그림자가 가장 무거운 부분이다.
- Android(차량 화면)에서는 아직 돌려 보지 않았다.

## 다시 만들기

맵은 Python 생성기가 만든다. Python에는 numpy, shapely, scipy, matplotlib, mapbox_earcut, Pillow가 필요하다.

1. `tools/network.py`: 도로망, 교통 차 줄, 신호를 만든다(`Editor/Data/Landmark*.json`).
2. `tools/build_district.py <구역>`: 구역 메시를 `C:/c/busan-out/<구역>`에 만든다. 구역은 `ring`, `gwangalli`, `bridge`, `haeundae`, `seomyeon`을 먼저 만들고, 그다음 `infill`, 마지막에 `trees` 순서로 만든다.
3. Unity에서 `BusanEnvironment.Editor.BusanDistrictBuilder.Build(<구역>, <출력 폴더>)`로 구역마다 프리팹을 만든다(출력 폴더 기본값은 `C:/c/busan-out/<구역>`, 2단계와 같은 곳).
4. Unity 메뉴 `Busan → Landmarks → Build assembled scene`: 구역을 모아 씬을 조립한다.

작업 기록과 검사 방법은 저장소 루트의 `implementation-notes.md`에 있다.

## 출처

- 도로와 해안선 위치: © OpenStreetMap contributors, ODbL 1.0
- 도로·보도 재질과 가로수 모델: Poly Haven(CC0)
- 차량 모델: 독립 제작자의 3D 모델(CC BY 4.0)이며 현대자동차 공식 자산이 아니다. 제작자와 변경 사항은 [팀원 인수인계](team-handoff.md)의 차량 모델 출처에 있다.
- 건물과 간판은 생성기가 만든 것이며 실제 상호를 쓰지 않았다.
