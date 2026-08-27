# E2E Runbook — 8 op mới Tier-1 (bản 28 ops)

> Chạy **sau khi** install bản mới + restart Revit 1 lần. Dùng MCP tools `revit_*` (đã
> lazy-load) hoặc pipe client. Mỗi bước: `validate` → `preview` → `commit` → `confirm` →
> `job.status` → check assertions.

## Bước 0 — Xác nhận add-in bản mới đã nạp

1. `revit_get_capabilities` — đếm số op. **Phải thấy 28 op**, trong đó có:
   `family.load`, `family.instance.create`, `column.create`, `beam.create`, `slab.create`,
   `roof.create`, `view.create_section`, `view.create_elevation`.
   Nếu chỉ 20 op → Revit chưa nạp bản mới (chưa restart / chưa install).
2. `revit_inspect_document` — document hoạt động, có Level cho `active_view_level`.

## Bước 1 — Chạy probe plan

Plan: [probe-new-ops.json](probe-new-ops.json) — tạo 1 phần tử probe cho 7 op (family.load
không probe được vì cần path .rfa thật):

| op | phần tử | assert |
|---|---|---|
| `column.create` | cột tại (1000,1000) | element_exists |
| `family.instance.create` | instance tại (2000,1000) | element_exists |
| `beam.create` | dầm dài 4000 tại y=3000 | element_exists + length 4000±2 |
| `slab.create` | sàn 3000×2000 | element_exists |
| `roof.create` | mái 4000×3000 | element_exists |
| `view.create_section` | mặt cắt box 12×12×5m | element_exists |
| `view.create_elevation` | mặt đứng box dọc | element_exists |

`requireUserConfirmation: true` → sau `plan.commit` sẽ `AwaitingConfirmation` → gọi
`plan.confirm` (token) → `job.status` đến `Completed`.

> [!IMPORTANT]
> `column.create`/`family.instance.create` cần **có sẵn family cột** trong template
> (`project_default_or_fail`, category `OST_Columns`). Nếu template trống → op fail với
> `TYPE_NOT_FOUND`. Khi đó dùng `family.load` với 1 file .rfa cột trước, rồi chạy lại.

## Bước 2 — Dọn probe

1. `revit_query_elements` lọc theo tên `E2E_Probe_*` (7 phần tử) → lấy `uniqueId` của mỗi cái.
2. Dán uniqueId vào [cleanup-probe.json](cleanup-probe.json) (thay `PASTE_UNIQUEID_*`).
3. `plan.validate` → `plan.commit` → `plan.confirm` → `job.status` = `Completed`.
4. `revit_query_elements` lại theo `E2E_Probe_*` → phải **trống**.

## Bước 3 — Kết luận

- 7/7 op probe + cleanup pass → **Tier-1 E2E OK** → sang Phase 4 (build nhà).
- Op nào fail → đọc `job.status`.errors (code + message) → fix op → build → install → restart → chạy lại op đó.

## Ghi chú

- `family.load`: test riêng khi có file `.rfa` (vd cột/dầm/thang) → args `{ "path": "C:\\...\\cot.rfa", "symbolName": "Cột 300x300" }`.
- `stairs.create` **không build** (Stairs.Create ABSENT) — nếu cần thang, dùng family instance thang.
