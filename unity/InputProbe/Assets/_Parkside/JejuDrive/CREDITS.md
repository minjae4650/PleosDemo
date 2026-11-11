# Jeju drive data and assets

Road data: **© OpenStreetMap contributors**, available under the [Open Database License 1.0](https://opendatacommons.org/licenses/odbl/1-0/). Attribution: https://www.openstreetmap.org/copyright

The extracted source roads (`Data/SourceRoads.geojson`) and the geographic route derivative (`Data/JejuRoadData.json`, geographic route content) are made available under ODbL 1.0. Original way IDs and road tags are retained. The original query covered 33.22–33.39°N, 126.37–126.56°E. Downloaded on 2026-11-06. The Unity C# implementation and scene arrangement are separate from the geographic database.

Elevation: [Mapzen Terrain Tiles on AWS](https://registry.opendata.aws/terrain-tiles/), Terrarium PNG, zoom 12. Accessed 2026-11-06. [Format](https://github.com/tilezen/joerd/blob/master/docs/formats.md), [attribution](https://github.com/tilezen/joerd/blob/master/docs/attribution.md).

Global SRTM and GMTED2010 terrain data courtesy of the U.S. Geological Survey; global ETOPO1 terrain data courtesy of the U.S. National Oceanic and Atmospheric Administration. Mapzen/Terrain Tiles combines source datasets; source-specific attribution remains applicable. The scene uses a local equirectangular approximation, not a surveying coordinate system. DEM and route heights are approximate and smoothed for driving.

Existing project assets reused without modifying their originals:

- `ParkingLotEnvironment/RealVehicles/hyundai-tucson/hyundai-tucson.prefab`
- `ParkingLotEnvironment/RealVehicles/hyundai-tucson-lod1`, `hyundai-i30-lod1`, `hyundai-azera-lod1` (ambient traffic; existing project models)
- `ParkingLotEnvironment/RealVehicles/tree-small-far/tree-small-far.prefab`
- `ParkingLotEnvironment/RealVehicles/tree-small-real/tree-small-real.prefab`
- `_Parkside/Lookdev/Models/shrub_01.fbx` and its existing scanned textures/material
- `ParkingLotEnvironment/KoreanAssets/forest_crown_cutout.png`, `forest_canopy.png`, concrete/grass textures
- `ParkingLotEnvironment/CloudPilot/Canopy/LeafDoubleSided.*`
- `ParkingLotEnvironment/RealismAssets/clean_asphalt_*`
- `ParkingLotEnvironment/KoreanAssets/aerial_grass_rock_*`
- `ParkingLotEnvironment/Materials/Parking_sky.mat`
- `ParkingLotEnvironment/CloudPilot/Canopy/MapCameraFXAA.*`
- `_Parkside/UI/Fonts/AstaSans-Medium.ttf` (SIL OFL; see adjacent OFL.txt)

Original model/texture credits remain in `ParkingLotEnvironment/CREDITS.md` and its source/import records. Reusing existing project assets does not change their original license conditions. The houses, basalt markers, guardrails and water shader are scene-specific constructions; their placement is illustrative rather than a surveyed reconstruction.

Expansion sources: `Data/ContextGeography.geojson`, `Data/OriginalCoastalRoute.json`, and the geographic content of `Data/JejuRoadData.json` are ODbL geographic derivatives. The bounded OSM map extracts and their SHA-256 hashes are documented in `Data/ContextSources.json`. Building outlines use OSM footprints; heights without explicit tags, facades, vegetation arrangements, stone walls and street furnishings are illustrative.

Traffic controls: existing OSM junction node positions anchor seven simulated junction controls. Signal mapping in these extracts is incomplete. Lamp placement, lane markings and the 64-second demonstration cycle are authored game content, not surveyed municipal installations or live timing data. Palm, grass and basalt geometry is generated for this scene; no new third-party plant models were downloaded.

The directed traffic database (`Data/JejuTrafficNetwork.json`, including OSM way/node IDs, one-way, speed, lane tags and restriction relations) is also an ODbL 1.0 geographic derivative. Ten bounded API extracts extend the detailed southern context eastward to Gangjeong. NPC vehicle behaviour and illustrative junction surface geometry are scene-specific code and arrangement. No Google or Kakao map imagery was copied into the scene.

Coastal landmark upgrade (2026-11-07): the four scenic plazas, wave/wind sculptures, dolhareubang and haenyeo interpretations, furniture and garden arrangements are original scene-specific procedural geometry. They reuse existing paving and rock textures. They are illustrative additions, not surveyed public installations or copies of specific modern artworks. The columnar-coast interpretation uses the existing DEM/OSM geography and the location described by [VisitJeju](https://www.visitjeju.net/kr/detail/view?contentsid=CNTS_000000000020476). Traditional dolhareubang proportions and identifying features were referenced from the [Korea Heritage Service](https://digital.khs.go.kr/heri/heriDetail.do?ctptNo=2443900024500&ctptUid=13898859739451806245). No reference photographs or third-party landmark models were imported.


Seogwipo district upgrade (2026-11-10) uses the existing ODbL extract of the Beophwan–Seoho area for streets, building outlines and tagged signal nodes. Building heights use source tags where present and estimates by building use elsewhere. Storefront names, materials and landscaping are scene-specific. The stadium's footprint is OSM way 175919449. The crescent roof is a simplified model informed by the [stadium structural designer](https://www.thorntontomasetti.com/project/jeju-world-cup-stadium). The city hall annex uses OSM way 281765558 at the location listed by [Seogwipo City](https://parking.seogwipo.go.kr/period/list).


Road-view study (2026-11-10). Five Kakao Map panoramas photographed in March 2026 were viewed as references for façade profiles, shop bands, paving and road markings. The scene uses authored meshes and project materials. The supplemental footprint subset is from Overture Maps Foundation Buildings release 2026-10-21.0 (ODbL), with GERS identifiers and the source hash in `Data/SeogwipoStreetReference.json`. [Overture attribution and licensing](https://docs.overturemaps.org/attribution/) · [Study locations](../../../../../docs/jeju-drive.md)
