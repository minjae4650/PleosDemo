# 파리 에펠탑·샹젤리제 주행

Unity 2022.3.16f1에서 `Assets/_Parkside/Scenes/ParisEiffelDrive.unity`를 열고 Play한다. `PARKSIDE → Paris → Open Eiffel drive` 메뉴에서도 열 수 있다.

- `W/S` 가속·후진, `A/D` 조향, `Space` 제동, `C` 카메라, `R` 도로 복귀, `G` 풍경 주행, `Tab` 안내 전환.
- `1` 뤼 장 레, `2` 트로카데로, `3` 비르아켐 다리, `4` 에콜 밀리테르, `5` 알마 다리, `6` 개선문, `7` 샹젤리제, `8` 콩코르드, `9` 앵발리드.
- 11.39km 안내 코스는 에펠탑 주변에서 에콜 밀리테르와 앵발리드를 거쳐 콩코르드 다리를 건넌다. 샹젤리제와 개선문을 돌아 클레베르 대로와 알마 다리, 트로카데로, 비르아켐 다리를 연결한다. 이에나 다리는 파리시가 지정한 보행 공간으로 표현했다. [파리시의 이에나 다리 안내](https://www.paris.fr/pages/paris-se-transforme-ce-qui-a-change-en-2024-dans-le-16e-arrondissement-30217)
- 철골과 전망대는 에펠탑의 330m 높이와 125m 밑변을 참고해 모델링했다. [에펠탑 공식 치수](https://www.toureiffel.paris/fr/le-monument/chiffres-cle)
- 도로 위치, OSM에 등록된 일방통행과 안내 경로의 노드 단위 회전 제한, 건물 윤곽, 센강과 교량 윤곽은 OpenStreetMap을 바탕으로 한다. 건물 높이 태그가 없으면 층수를 추정한다. 노면은 도심 주행용 평탄 모델이다. 개선문, 앵발리드의 돔, 콩코르드의 오벨리스크와 분수는 지도 위치에 맞춰 모델링했다. [개선문 공식 치수](https://www.paris-arc-de-triomphe.fr/enseignants/ressources-pedagogiques/documents/dossier-pedagogique-l-arc-de-triomphe) · [앵발리드 돔](https://www.musee-armee.fr/magazine/patrimoine-le-lanternon.html) · [파리시 콩코르드 광장 자료](https://www.paris.fr/pages/la-concorde-une-place-aux-multiples-facettes-22106)

재생성하려면 Python에서 Pillow 및 Shapely 2.1 이상과 함께 `tools/prepare-paris-drive.py`를 실행한다. 원본 OSM 추출 파일은 `build/paris-drive/`, 요청 URL과 SHA-256은 `ParisRoadData.json`에 저장한다. 지리 데이터는 © OpenStreetMap contributors, ODbL 1.0이다. Unity 배치 진입점은 `Parkside.Paris.Editor.ParisDriveBuilder.BuildBatch`이며 같은 파리 씬을 저장하고 다시 열어 도로 표면 및 주행을 검사한다. `build/paris-drive/verification.txt`와 `paris-*.png`에서 결과를 확인한다.

## 장 레 거리와 가로 시설

장 레 거리의 [풀먼(22번)](https://all.accor.com/hotel/7229/index.fr.shtml)과 [메르큐르(20번)](https://all.accor.com/hotel/2175/index.fr.shtml)는 지도 윤곽에 현대식 창호와 서로 다른 입구를 제작했다. [에밀 앙투안 체육시설(9번)](https://www.paris.fr/lieux/centre-sportif-emile-anthoine-3056) 쪽은 OSM 시설 경계(way 240774660)를 바탕으로 진녹색 울타리와 시설 표지를 배치했다. 간판 형상과 시설 세부 위치는 주행 화면용으로 재구성했다.

[파리시 가로 시설 도감](https://cdn.paris.fr/paris/filer/35052_VDP_Manifeste_esthetique_Livre_3_BD.pdf)과 [카르나발레 박물관의 모리스 기둥](https://www.parismuseescollections.paris.fr/fr/musee-carnavalet/oeuvres/la-colonne-morris)을 참고해 파란 거리명 표지, 진녹색 가로등·볼라드·벤치와 원통형 광고 기둥을 제작했다. 노면에는 보수 패치와 주철 덮개를 추가했다.

양안 도로의 상점은 OSM에서 건물 윤곽 안에 표시된 상점의 이름과 용도를 우선 사용한다. 상점 정보가 비어 있으면 빵집, 카페, 꽃집, 소규모 상점으로 연출한다. 건물 단위의 위치는 지도 자료를 따르며 같은 건물 안의 개별 출입구와 점포 폭은 주행 씬에 맞춰 배치한다.

도로를 향한 외벽의 창호와 출입구는 벽을 나눠 개구부를 내고, 유리와 문짝을 그 안으로 물렸다. 상가 전면은 반투명 유리 뒤에 바닥, 진열대, 안쪽 벽을 모델링했다. 주요 경로에 가까운 건물에는 층별 발코니와 돌출 난간, 지붕창과 굴뚝을 추가했다. 층별 발코니와 지붕 구성은 파리시가 설명하는 오스만식 건물 구성을 참고했다. [파리시 건축 자료](https://www.paris.fr/pages/haussmann-l-homme-qui-a-transforme-paris-23091)

외벽만 다시 생성하려면 `Parkside.Paris.Editor.ParisDriveBuilder.RefreshBuildingsBatch`를 사용한다. `paris-building-depth.png`와 `paris-shop-depth.png`는 가까운 사선 시점에서 촬영한 씬 캡처다.
가로 시설과 외벽 검사 결과는 `build/paris-drive/street-finish.txt`, `facade-coverage.txt`에 있다. 확장 구간의 도로 높이 캡처는 `paris-champs-elysees.png`, `paris-arc-de-triomphe.png`, `paris-concorde.png`, `paris-invalides.png`다.

## 신호와 주변 차량

OpenStreetMap의 교차로 신호 위치에 프랑스식 세로형 신호등과 진입 방향별 정지선을 배치했다. 인접한 교차로 노드는 같은 신호 주기를 사용하며, 신호 시간은 게임용으로 설정했다. [프랑스 도로 신호 기준](https://www.legifrance.gouv.fr/jorf/id/JORFTEXT000000394052)

주변 차량은 실제 지도 도로의 방향과 노드 단위 회전 제한을 따르고, 서로 다른 목적지로 이동한다. 진입로와 회전 경로는 Unity 아스팔트 위에 바퀴가 놓이는지 확인한 뒤 교통망에 포함한다. 차량은 신호와 앞차에 맞춰 속도를 줄이고 정지하며, 플레이어에서 멀어지면 풀로 돌아간다.

`tools/prepare-paris-drive.py --traffic-only`로 저장된 도로를 기준으로 교통망 원본을 갱신한다. Unity 배치 진입점 `Parkside.Paris.Editor.ParisDriveBuilder.RefreshTrafficBatch`가 차로와 신호를 다시 배치하고 Play Mode에서 검사한다. 결과는 `build/paris-drive/paris-traffic-play.txt`에 기록한다.
