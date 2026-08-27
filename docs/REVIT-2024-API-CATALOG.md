# Revit 2024 — API Feature Catalog (verified)

> Nguồn ground truth: **compile probe** (csc) + **metadata TypeDef** scan trực tiếp trên
> `D:\REVIT\2024\Revit 2024\RevitAPI.dll` — chính bộ DLL mà Revit 2024 đang chạy (PID thật).
> Không đoán: mỗi dòng dưới đây đã được xác minh bằng build 0/0 hoặc scan TypeDef chính xác
> (khác với audit cũ — audit cũ chỉ quét namespace 1-level `Autodesk.Revit.DB.*` nên **nhầm**
> các type nằm trong `Architecture` / `Structure` là "thiếu").
>
> **Trạng thái** mỗi chức năng:
> - ✅ **Có op** — đã có operation trong add-in, build + dispatch xong.
> - 🟢 **API có, chưa làm op** — API hoạt động (verified), có thể implement op sau.
> - 🟡 **API có nhưng bị chặn phần nào** — factory phụ thiếu (vd không tạo được parent).
> - 🔴 **Bị chặn** — type/factory KHÔNG có trong build này (compile fail / crash runtime).

---

## 0. Cấu trúc namespace quan trọng (bài học audit)

Revit API phân type theo **sub-namespace**, KHÔNG phải tất cả nằm trong `Autodesk.Revit.DB`:

| Sub-namespace | Chứa (verified có) |
|---|---|
| `Autodesk.Revit.DB` (top) | `Wall`, `WallType`, `Level`, `Grid`, `Ceiling`, `CeilingType`, `RoofType`, `FootPrintRoof`, `ExtrusionRoof`, `Floor`, `BuildingPad`, `WallFoundation`, `ViewSection`, `ViewPlan`, `View3D`, `ViewFamilyType`, `ViewSheet`, `Group`, `GroupType`, `DetailCurve`, `ModelCurve`, `ModelText`, `TextNote`, `Leader`, `Dimension`, `ImportInstance`, `Rebar`, `RebarShape`, `Material`, `Phase`, `Workset`, `Sketch` |
| `Autodesk.Revit.DB.Architecture` | **`Stairs`, `StairsRun`, `StairsLanding`, `MultistoryStairs`, `StairsType`, `Railing`, `RailingType`, `Room`, `RoomTag`, `RoomTagType`** |
| `Autodesk.Revit.DB.Structure` | **`Truss`, `TrussType`, `TrussMemberInfo`, `TrussMemberType`** + `Analytical*` (chỉ data, không element) |
| `Autodesk.Revit.Creation` | `Document` (factory `New*`), `Application` |

> [!IMPORTANT]
> Khi audit API, **luôn scan theo tên TypeDef exact + kể cả sub-namespace**. Audit chỉ quét
> 1-level namespace sẽ bỏ sót toàn bộ `Architecture.*` và `Structure.*`.

---

## 1. Tường (Wall)

| Chức năng | API thật (verified) | Trạng thái | Ghi chú |
|---|---|---|---|
| Tạo tường thẳng | `Wall.Create(Document, Curve, ElementId levelId, bool structural)` | ✅ `wall.create` | op hiện tại |
| Tạo tường có type+chiều cao | `Wall.Create(Document, Curve, ElementId wallTypeId, ElementId levelId, double height, double offset, bool flip, bool structural)` | ✅ | 8-arg — dùng cho **dầm** |
| Tạo tường profile | `Wall.Create(Document, IList<Curve> profile, ElementId wallTypeId, ElementId levelId, bool structural[, XYZ normal])` | 🟢 | 5 overload |
| Đổi type tường | `Wall.WallType` (RW), `wall.ChangeTypeId(ElementId)` | ✅ `wall.update` | |
| Đổi chiều cao/tầm | `Parameter` `WALL_USER_HEIGHT_PARAM`, `WALL_BASE_OFFSET`, `WALL_TOP_OFFSET` | ✅ `wall.update` | |
| **Đổi bề dày tường** | `Wall.Width` — **READ-ONLY** (verified) | 🔴 | Phải đổi **WallType** (type cũng RO width) → muốn thay bề dày phải tạo/sửa WallType |
| Tường structural | `Wall.Create(..., structural: true)` + `Wall.StructuralUsage` (RW, type `StructuralWallUsage` **ABSENT** → không gán được) | 🟡 | Flag structural có, enum usage thiếu |
| **Curtain wall** | `CurtainWall` / `CurtainWallGrid` / `CurtainWallPanel` — 🔴 ABSENT | 🔴 | Bị chặn |

---

## 2. Trục / Cao độ (Grid / Level)

| Chức năng | API thật (verified) | Trạng thái | Ghi chú |
|---|---|---|---|
| Tạo level | `Level.Create(Document, double elevation)` | ✅ `level.create` | |
| Đổi cao độ | `Level.Elevation` (RW) | 🟢 | |
| Tạo trục thẳng | `Grid.Create(Document, Line)` | ✅ `grid.create` | |
| Tạo trục cong | `Grid.Create(Document, Arc)` | 🟢 | |
| Đổi tên trục | `Grid.Name` (RW) | 🟢 | |
| Chiều dài trục dọc | `Grid.SetVerticalExtents(double bottom, double top)` | 🟢 | |
| Trục bubble | `GridBubble` — 🔴 ABSENT | 🔴 | |
| Reference plane | `ReferencePlane` (type có) nhưng **`ReferencePlane.Create` ABSENT** (verified) | 🔴 | Không tạo được ref plane mới |

---

## 3. Cột / Dầm / Khung (Column / Beam / Framing)

> [!CAUTION]
> **Các class element native `Column`, `Beam`, `StructuralColumn`, `StructuralWall`,
> `StructuralFraming`, `StructuralConnector` — TẤT CẢ ABSENT** (verified TypeDef + compile).
> Đây là ràng buộc CỨNG của bộ DLL này. → dùng **workaround** bên dưới.

| Chức năng | Cách trong build này | API thật | Trạng thái |
|---|---|---|---|
| **Cột** | family instance structural, category `OST_Columns` | `Document.Create.NewFamilyInstance(XYZ, FamilySymbol, StructuralType.Column)` (3-arg, verified) | ✅ `column.create` |
| **Dầm** | structural Wall (dầm giằng), depth = `WALL_USER_HEIGHT_PARAM`, width do WallType quyết định | 8-arg `Wall.Create` | ✅ `beam.create` |
| Dầm theo family | family instance trên curve | `NewFamilyInstance(Curve, FamilySymbol, Level, StructuralType.Beam)` (4-arg curve, verified) | 🟢 |
| Truss | `Truss.Create(Document, ElementId trussTypeId, ElementId sketchPlaneId, Curve)` — `Truss` trong `Structure` | verified | 🟢 |
| **Analytical model** | `AnalyticalModel` 🔴 ABSENT; chỉ có `Analytical*` data types | | 🔴 |
| Rebar (sắt) | `Rebar` / `RebarShape` / `RebarBarType` — type **có** | verified | 🟢 (chưa làm op) |

`StructuralType` enum = **chỉ 3 giá trị**: `NonStructural`, `Column`, `Beam` (KHÔNG có `.Structural`, verified).

---

## 4. Sàn (Floor / Slab)

| Chức năng | API thật (verified) | Trạng thái | Ghi chú |
|---|---|---|---|
| **Sàn bằng slab** | `Slab` / `SlabType` 🔴 ABSENT | 🔴 | → workaround bằng Ceiling |
| **Sàn (workaround)** | `Ceiling.Create(Document, IList<CurveLoop>, ElementId ceilingTypeId, ElementId levelId)` | ✅ `slab.create` | Ceiling đặt TRÊN level = sàn tầng đó (pattern Revit chuẩn) |
| `CeilingType` | `FilteredElementCollector().OfClass(typeof(CeilingType))` | ✅ | resolve type đầu tiên |
| Building pad | `BuildingPad.Create(Document, ElementId padTypeId, ElementId levelId, IList<CurveLoop>)` | 🟢 | |
| **`CurveLoop`** | **KHÔNG có ctor 1-tham-số** (verified) → `new CurveLoop()` + `Append(Curve)` | | bài học probe |

---

## 5. Mái (Roof)

| Chức năng | API thật (verified) | Trạng thái | Ghi chú |
|---|---|---|---|
| Mái footprint | `Document.Create.NewFootPrintRoof(CurveArray, Level, RoofType, **out** ModelCurveArray)` | ✅ `roof.create` | mapping là **out param** |
| Overhang | `FootPrintRoof.set_Overhang(ModelCurve, double)` (per-curve) | ✅ | không có property `Overhang` trực tiếp |
| **`CurveArray` / `ModelCurveArray`** | **`new ...()` + `Append`** (type `Application` ABSENT, không có `NewCurveArray()` factory) | | bài học probe |
| Mái extrusion | `NewExtrusionRoof(CurveArray, ReferencePlane, Level, RoofType, double, double)` — type có nhưng cần `ReferencePlane` (mà `ReferencePlane.Create` ABSENT) | 🟡 | Chặn do thiếu ref plane |
| `Roof` (base) | `Roof` 🔴 ABSENT; chỉ có `RoofBase`, `RoofType`, `FootPrintRoof`, `ExtrusionRoof` | | |
| Gutter / Fascia | type **có** | 🟢 | chưa làm op |

---

## 6. Cầu thang / Lan can (Stairs / Railing)

> [!WARNING]
> Type `Stairs`, `StairsRun`, `StairsLanding`, `MultistoryStairs`, `Railing` **CÓ** (namespace
> `Architecture`) và factory `StairsRun.CreateStraightRun` / `CreateSpiralRun`,
> `StairsLanding.CreateSketchedLanding`, `MultistoryStairs.Create`, `Railing.Create` **đều compile**.
> **NHƯNG `Stairs.Create` KHÔNG TỒN TẠI** (verified cả 2 signature thử) → **không tạo được
> parent `Stairs`** → toàn bộ chuỗi tạo thang **bị chặn** vì `StairsRun`/`StairsLanding` đều
> cần 1 parent `Stairs` (ElementId).

| Chức năng | API thật (verified) | Trạng thái | Ghi chú |
|---|---|---|---|
| **Tạo parent Stairs** | `Stairs.Create` 🔴 **ABSENT** (verified) | 🔴 | **Blocker** — không có đường tạo parent |
| Chạy thang | `StairsRun.CreateStraightRun(Document, ElementId stairsId, Line, StairsRunJustification)` | 🟡 | Có API nhưng cần parent |
| Thang xoắn | `StairsRun.CreateSpiralRun(Document, ElementId, XYZ, double r, double a1, double a2, bool, StairsRunJustification)` | 🟡 | cần parent |
| Sườn/landing | `StairsLanding.CreateSketchedLanding(Document, ElementId stairsId, CurveLoop, double)` | 🟡 | cần parent |
| Multistory | `MultistoryStairs.Create(Stairs)` | 🟡 | cần parent |
| Lan can | `Railing.Create(Document, CurveLoop, ElementId railingTypeId, ElementId baseLevelId)` → trả `Railing` | 🟢 | **Có thể dùng độc lập** (không cần Stairs) |
| **Fallback thang** | `NewFamilyInstance` với family thang (.rfa) | 🟢 | cần file family; **khuyến nghị** nếu cần thang |

> Kết luận: `stairs.create` **không build được** bằng API native (thiếu `Stairs.Create`).
> Nếu project cần thang → dùng **family instance thang** (cần .rfa) → op `family.instance.create`.

---

## 7. Cửa / Cửa sổ (Door / Window)

| Chức năng | API thật (verified) | Trạng thái | Ghi chú |
|---|---|---|---|
| Cửa | `Door` class 🔴 ABSENT → dùng `NewFamilyInstance` hosted | ✅ `door.insert` | pattern đã chạy OK |
| Cửa sổ | `Window` class 🔴 ABSENT → dùng `NewFamilyInstance` hosted | ✅ `window.insert` | 4-arg `(XYZ, symbol, host, structural)` verified |
| Family instance hosted | `NewFamilyInstance(XYZ, FamilySymbol, Element host, Level, StructuralType)` (5-arg) + `(XYZ, symbol, host, structural)` (4-arg) | ✅ | cả 2 overload verified |
| Mở lỗ (opening) | `Document.Create.NewOpening(Wall, XYZ, XYZ)` / `NewOpening(Element, CurveArray, bool)` | 🟢 | |

---

## 8. Family (load / instance / symbol)

| Chức năng | API thật (verified) | Trạng thái | Ghi chú |
|---|---|---|---|
| Load family | `Document.LoadFamilySymbol(String path, String name, out FamilySymbol)` | ✅ `family.load` | `LoadFamily(String, out Family)` 🔴 ABSENT (chỉ có dạng `LoadFamily(Document,...)`) |
| Instance tại điểm | `NewFamilyInstance(XYZ, FamilySymbol, StructuralType)` (3-arg) | ✅ `family.instance.create` | verified |
| Instance hosted | 4-arg / 5-arg (trên) | ✅ `window/door.insert` | |
| Instance trên curve | `NewFamilyInstance(Curve, FamilySymbol, Level, StructuralType)` | 🟢 | verified |
| `FamilyManager` | type **có** (create .rfa mới) | 🟢 | chưa làm op |
| `FamilySymbol.Activate()` | có | ✅ | dùng trong các op instance |

---

## 9. Phòng / Không gian (Room / Space / Area)

| Chức năng | API thật (verified) | Trạng thái | Ghi chú |
|---|---|---|---|
| Tạo phòng | `Document.Create.NewRoom(Level, UV)` — `Room` trong `Architecture` | ✅ `room.create` | |
| Room tag | `Document.Create.NewRoomTag(LinkElementId, UV, ElementId viewId)` | 🟢 | |
| Space / Area | `Space`, `SpaceTag`, `Area`, `AreaScheme` — type **có** | 🟢 | chưa làm op |

---

## 10. View / Sheet (mặt bằng, cắt, đứng, sheet)

| Chức năng | API thật (verified) | Trạng thái | Ghi chú |
|---|---|---|---|
| Mặt bằng | `ViewPlan.Create(Document, ElementId vftId, ElementId levelId)` | ✅ `view.create_plan` | |
| Mặt cắt | `ViewSection.CreateSection(Document, ElementId vftId, BoundingBoxXYZ)` | ✅ `view.create_section` | **KHÔNG có `ViewSection.Create`** (verified) |
| Chi tiết (detail) | `ViewSection.CreateDetail(Document, ElementId vftId, BoundingBoxXYZ)` — thay `ViewDetail` (🔴 ABSENT) | 🟢 | detail = section box nhỏ |
| Mặt đứng | `ViewSection.CreateSection` với box dọc + `ViewFamily.Elevation` | ✅ `view.create_elevation` | |
| 3D | `View3D.CreateIsometric` / `CreatePerspective` | 🟢 | |
| **`BoundingBoxXYZ`** | **chỉ có `Min`/`Max` (RW)** — `Workplane`/`BoundsType` 🔴 ABSENT (verified) | | |
| `ViewFamilyType` / `ViewFamily` | đầy đủ (FloorPlan/Section/Elevation/Detail/3D/AreaPlan...) | ✅ | resolve trong các op view |
| Sheet | `ViewSheet.Create(Document, ElementId titleBlockTypeId)` — `Sheet` class 🔴 ABSENT → dùng `ViewSheet` | ✅ `sheet.create` | |
| Đặt view lên sheet | `ViewSheet` + `Viewport` (type có) | ✅ `sheet.place_view` | |
| `ViewSheetType` / `DraftingView` / `ViewTemplate` | 🔴 ABSENT | 🔴 | |

---

## 11. Annotation / Detail (gạch, ký hiệu, chú thích)

| Chức năng | API thật (verified) | Trạng thái | Ghi chú |
|---|---|---|---|
| Model curve | `ModelCurve` (type có) — tạo qua `NewModelCurve...` | 🟢 | |
| Detail curve | `Document.Create.NewDetailCurve(View, Curve)` (type `DetailCurve` có) | 🟢 | |
| Model text | `ModelText` (type có) — tạo runtime-test | 🟡 | |
| Text note | `TextNote` (type có); `NewTextNote`/`NewLeader` **KHÔNG trên `Document.Create`** → dùng family note | 🟡 | |
| Dimension | `Dimension` (type có) | 🟢 | |
| **RasterImage / Underlay / PointCloud** | 🔴 **TẤT CẢ ABSENT** | 🔴 | ảnh nền / point cloud bị chặn |
| `Sketch` | type **có** | 🟢 | |

---

## 12. Group (nhóm)

| Chức năng | API thật (verified) | Trạng thái | Ghi chú |
|---|---|---|---|
| Tạo group | `Document.Create.NewGroup(ICollection<ElementId>)` | ✅ probe verified | chưa làm op |
| Ungroup | `Group.UngroupMembers()` | 🟢 | |
| `GroupType.LoadFrom` | có | 🟢 | |

---

## 13. Import / Export

| Chức năng | API thật (verified) | Trạng thái | Ghi chú |
|---|---|---|---|
| Import DWG | `ImportInstance.Create(Document, View, path, DWGImportOptions, ...)` / `Document.Link` | 🟢 | |
| Import SAT/STL/OBJ/SKP/3DM | `Document.Import/Link` (nhiều overload, verified) | 🟢 | |
| Export PDF | `Document.Export(folder, IList<ElementId>, PDFExportOptions)` | ✅ `export.pdf` | |
| Export DWG | ✅ `export.dwg` | ✅ | |
| Save / SaveAs | ✅ `document.save` / `document.save_as` | ✅ | |

---

## 14. MEP (đường ống / gió / điện)

> Type **có** (verified): `MEPModel`, `Pipe`/`PipeType`, `Duct`/`DuctType`, `FlexPipe`/`FlexDuct`,
> `Connector`/`ConnectorSet`, `MechanicalSystem`, `PipingSystem`, `LightingFixture`.
> Fitting factories **có trên `Document.Create`**: `NewElbowFitting`, `NewTeeFitting`,
> `NewUnionFitting`, `NewCrossFitting`, `NewTransitionFitting` (verified trong factory dump).
>
> Trạng thái: 🟢 **API có, chưa làm op** (phase sau). `Equipment`/`AirTerminal`/`SanitaryFixture` 🔴 ABSENT.

---

## 15. Material / Phase / Workset

| Nhóm | API (verified) | Trạng thái |
|---|---|---|
| Material | `Material` (type có), `Document.Paint(ElementId, Face, ElementId matId)` | 🟢 |
| Phase | `Phase`, `PhaseFilter`, `Document.Phases` | 🟢 |
| Workset | `Workset`, `WorksetId` (chỉ có `.IntegerValue`) | 🟢 |

---

## 16. Thao tác element (dùng chung)

| Chức năng | API (verified) | Trạng thái |
|---|---|---|
| Xóa | `Element.Delete` | ✅ `element.delete` |
| Di chuyển | `Transform`/move | ✅ `element.move` |
| Xoay | rotate (element) | ✅ `element.rotate` |
| Đổi tên | `Element.Name` (RW) | ✅ `element.rename` |
| Đặt parameter | `Parameter.Set` (int/bool/double/string; **`Set(bool)` không có** → `Set(1/0)`) | ✅ `parameter.set` / `parameter.set_many` |
| Lấy element | `Document.GetElement(long uniqueId)`, `FilteredElementCollector` | ✅ |

---

## 17. Tổng hợp: BỊ CHẶN (Tier 3 — không build)

> [!CAUTION]
> Những cái này **không tồn tại trong build này** (verified TypeDef + compile). **Không** viết
> op cho chúng — compile không qua / crash runtime.

| Loại | Chi tiết | Workaround |
|---|---|---|
| Cột/Dầm native | `Column`/`ColumnType`/`Beam`/`BeamType`/`StructuralColumn`/`StructuralWall`/`StructuralFraming`/`StructuralConnector` | column = family instance; dầm = structural Wall |
| Sàn slab | `Slab`/`SlabType` | `Ceiling.Create` |
| **Cầu thang (parent)** | `Stairs.Create` ABSENT (chặn toàn bộ StairsRun/Landing/Multistory) | family instance thang |
| Curtain wall | `CurtainWall`/`CurtainWallGrid`/`CurtainWallPanel` | — (không có) |
| Point cloud / ảnh nền | `PointCloud`/`RasterImage`/`Underlay` | — (không có) |
| Ref plane | `ReferencePlane.Create` ABSENT | — (chặn NewExtrusionRoof) |
| Alignment | `Alignment` type ABSENT | `element.move/rotate` |
| ViewDetail / Sheet / DraftingView | ABSENT | `ViewSection.CreateDetail` / `ViewSheet` |
| Analytical model | `AnalyticalModel` ABSENT | — |
| `Wall.Width` set | READ-ONLY | đổi WallType |
| MEP equipment | `Equipment`/`AirTerminal`/`SanitaryFixture` ABSENT | dùng `Connector` + fitting factories |

---

## 18. Checklist op đã có trong add-in (Tier 0 + Tier 1)

**Tier 0 (đã có sẵn từ trước, 20 ops):** `wall.create`, `wall.update`, `door.insert`,
`window.insert`, `parameter.set`, `parameter.set_many`, `element.delete`, `element.move`,
`element.rotate`, `element.rename`, `room.create`, `view.create_plan`, `sheet.create`,
`sheet.place_view`, `document.save`, `document.save_as`, `export.pdf`, `export.dwg`,
`level.create`, `grid.create`.

**Tier 1 (mới implement trong phase này, 8 ops):** `family.load`, `family.instance.create`,
`column.create`, `beam.create`, `slab.create`, `roof.create`, `view.create_section`,
`view.create_elevation`.

**Tổng: 28 ops** (đã build 0/0, dispatch + register xong).
