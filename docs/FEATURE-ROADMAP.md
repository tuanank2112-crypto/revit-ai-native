# Feature Roadmap — Autodesk Native Agent (Revit 2024)

> Phân tầng chức năng theo **độ sẵn sàng đã xác minh** (compile + TypeDef trên chính DLL Revit 2024
> đang chạy). Chi tiết signature từng API: xem [REVIT-2024-API-CATALOG.md](REVIT-2024-API-CATALOG.md).
>
> Nguyên tắc: **chỉ build op cho API đã verified có + compile 0/0**. Cái bị chặn (Tier 3)
> KHÔNG build — ghi rõ workaround.

---

## Tier 0 — Đã có sẵn & chạy OK (20 ops)

> [!NOTE]
> Đã E2E 12/12 trong Revit thật ở session trước. Op mới (Tier 1) cần E2E lại sau khi install.

| Op | Dùng để làm nhà 2 tầng |
|---|---|
| `wall.create` / `wall.update` | tường 1F/2F |
| `door.insert` / `window.insert` | cửa CT-01…04, cửa sổ |
| `room.create` | phòng (P.KHÁCH, BẾP, WC…) |
| `view.create_plan` / `sheet.create` / `sheet.place_view` | bản vẽ |
| `level.create` / `grid.create` | cao độ + trục 1-4, A-D |
| `element.delete/move/rotate/rename`, `parameter.set(_many)` | chỉnh sửa |
| `document.save(_as)`, `export.pdf`, `export.dwg` | xuất file đối chiếu |

---

## Tier 1 — Build trong phase này (8 ops, ✅ đã compile 0/0 + dispatch)

| Op | API verified | Workaround nếu native thiếu |
|---|---|---|
| `family.load` | `Document.LoadFamilySymbol(path, name, out FamilySymbol)` | `LoadFamily(String,…)` ABSENT → dùng `LoadFamilySymbol` |
| `family.instance.create` | `NewFamilyInstance(XYZ, symbol, StructuralType)` (3-arg) | rotation không có (warning) |
| `column.create` | wrapper family.instance, `StructuralType.Column`, category `OST_Columns` | Column native ABSENT |
| `beam.create` | 8-arg `Wall.Create` structural (dầm giằng) | Beam native ABSENT; `Wall.Width` RO → width do WallType |
| `slab.create` | `Ceiling.Create(List<CurveLoop>, ceilingTypeId, levelId)` | Slab ABSENT; `CurveLoop` = `new`+`Append` |
| `roof.create` | `NewFootPrintRoof(CurveArray, Level, RoofType, out ModelCurveArray)` + `set_Overhang` | `NewExtrusionRoof` chặn (thiếu `ReferencePlane.Create`) |
| `view.create_section` | `ViewSection.CreateSection(doc, vftId, BoundingBoxXYZ{Min,Max})` | `ViewSection.Create` ABSENT |
| `view.create_elevation` | reuse section op, `ViewFamily.Elevation` | |

**Tổng: 28 ops.** Đã register trong `CommandRegistry`, dispatch trong `PlanExecutor`, build 0/0,
test 59/59.

---

## Tier 2 — API có, làm sau (có thể implement, chưa cần)

> [!TIP]
> Các op này **có thể** build (API verified compile) nhưng chưa làm vì không cần cho nhà 2 tầng
> cơ bản. Ưu tiên: group (tạo bộ cột+dầm lặp lại) > truss > rebar > MEP.

| Op đề xuất | API verified | Ghi chú |
|---|---|---|
| `group.create` | `Document.Create.NewGroup(ICollection<ElementId>)` | nhóm cột+dầm tầng lặp lại |
| `group.ungroup` | `Group.UngroupMembers()` | |
| `truss.create` | `Truss.Create(doc, trussTypeId, sketchPlaneId, Curve)` (`Structure.Truss`) | |
| `rebar.create` | `Rebar` / `RebarShape` | bê tông cốt thép |
| `railing.create` | `Railing.Create(doc, CurveLoop, railingTypeId, baseLevelId)` | **dùng độc lập được** (không cần Stairs) |
| `buildingpad.create` | `BuildingPad.Create(doc, padTypeId, levelId, List<CurveLoop>)` | sân nền |
| `import.dwg/sat/stl/…` | `Document.Import/Link` | đối chiếu bản vẽ CAD |
| MEP: `pipe.create`, `duct.create`, fitting | `NewElbowFitting/TeeFitting/UnionFitting/CrossFitting/TransitionFitting` | phase MEP |
| `wallfoundation` | `WallFoundation`/`WallFoundationType` (type có) | móng tường |
| `detail_curve.create`, `model_text.create` | `NewDetailCurve`, `ModelText` (runtime-test) | chú thích |
| `view.create_3d` | `View3D.CreateIsometric/CreatePerspective` | |
| `view.create_detail` | `ViewSection.CreateDetail` (thay ViewDetail) | |
| `gutter.create` / `fascia.create` | type có | phụ mái |

---

## Tier 3 — BỊ CHẶN (API trim — KHÔNG build)

> [!CAUTION]
> Không tồn tại trong build này (verified). Không viết op. Ghi workaround nếu có.

| Chức năng | Lý do chặn (verified) | Workaround |
|---|---|---|
| **Cầu thang** | `Stairs.Create` ABSENT → không tạo parent `Stairs`; `StairsRun`/`StairsLanding`/`MultistoryStairs` đều cần parent | **family instance thang** (.rfa) qua `family.instance.create` |
| Cột/Dầm native | `Column`/`Beam`/`StructuralColumn`/`StructuralWall`/`StructuralFraming` ABSENT | column=family, dầm=structural Wall |
| Sàn slab | `Slab`/`SlabType` ABSENT | `Ceiling.Create` |
| Curtain wall | `CurtainWall`/`CurtainWallGrid`/`CurtainWallPanel` ABSENT | — |
| Point cloud / ảnh nền | `PointCloud`/`RasterImage`/`Underlay` ABSENT | — |
| Reference plane | `ReferencePlane.Create` ABSENT (chặn `NewExtrusionRoof`) | — |
| Alignment | `Alignment` ABSENT | `element.move/rotate` |
| ViewDetail / Sheet / DraftingView | ABSENT | `ViewSection.CreateDetail` / `ViewSheet` |
| Analytical model | `AnalyticalModel` ABSENT | — |
| Đổi bề dày tường trực tiếp | `Wall.Width` + `WallType.Width` READ-ONLY | tạo/sửa WallType |
| MEP equipment | `Equipment`/`AirTerminal`/`SanitaryFixture` ABSENT | `Connector` + fitting |
| Grid bubble | `GridBubble` ABSENT | — |

---

## Thứ tự build nhà 2 tầng (Phase 4) — dựa vào Tier 0+1

Mỗi bước = 1 plan (assertions + `requireUserConfirmation`), TransactionGroup riêng để rollback
độc lập. Dữ liệu kích thước lấy từ decode vector session trước (grid_report/dim_chains/regions).

1. **Levels** — T1 `+0.000` (check Level 1 có sẵn), T2 `+3.900`, Mái `+7.600`.
2. **Grids** — trục 1-4 (10045+10045), A-D + nhịp chi tiết.
3. **Cột + Dầm tầng 1** — `column.create` + `beam.create` (dầm giằng).
4. **Tường 1F** — `wall.create` (cao 3900, vị trí từ `walls_1f.json`).
5. **Sàn 2F** — `slab.create` (outline 2F, trên level T1).
6. **Tường 2F** — `wall.create` (cao 3700, set-back).
7. **Cột + Dầm tầng 2**.
8. **Cửa + Cửa sổ** — `door.insert` CT-01…04 + `window.insert` (kích thước từ mặt đứng).
9. **Thang** — family instance thang (Tier 3 workaround) hoặc bỏ qua.
10. **Phòng + Views** — `room.create`, `view.create_plan` 1F/2F, `view.create_section`,
    `view.create_elevation`, `export.pdf` → đối chiếu bản vẽ gốc 9 trang.

> [!IMPORTANT]
> **Trước Phase 4 phải install + restart Revit 1 lần** để add-in nạp bản 28 ops (bản đang chạy
> trong Revit hiện tại vẫn là v1.0.0 cũ). Xem `scripts/install-revit2024.ps1`.
