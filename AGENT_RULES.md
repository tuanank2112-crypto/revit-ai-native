# AGENT_RULES.md

## Mục tiêu
Tối ưu số lượng request và context, nhưng không hy sinh độ chính xác.

## Cách làm việc
- Mỗi request phải làm được nhiều việc hữu ích nhất có thể.
- Ưu tiên batch read → batch edit → build.
- Không đọc từng file rồi dừng.
- Không search từng symbol nếu có thể mở trực tiếp file.
- Không build sau mỗi file.
- Khi có nhiều compile errors, nhóm theo root cause và sửa hàng loạt.
- Không narrate dài kiểu “bây giờ tôi sẽ…”.
- Không hỏi “có muốn tiếp tục không”.
- Không dừng sau từng subtask nhỏ.
- Workspace/source code hiện tại là source of truth.
- Chỉ đọc file liên quan, không scan lại toàn repo nếu không cần.
- Nếu đã đủ thông tin để sửa thì sửa ngay, để compiler xác nhận sau.

## Khi terminal/build lỗi
- Tự build/test/fix nếu terminal hoạt động.
- Nếu terminal lỗi hệ thống lặp lại, chỉ retry 1 lần.
- Sau đó tiếp tục code/static audit, không dừng project.
- Ghi lệnh cần chạy sau vào BUILD_QUEUE.md nếu cần.

## Context
- Đọc PROJECT_STATE.md khi bắt đầu chat mới.
- Không lặp lại lịch sử chat cũ.
- Khi context quá lớn, cập nhật PROJECT_STATE.md rồi tiếp tục ở chat mới.

## Quy tắc chất lượng
- Không fake success.
- Không để stub/NotImplementedException trong capability SUPPORTED.
- Revit API chỉ chạy trên main thread qua ExternalEvent.
- Giữ Revit 2024 + .NET Framework 4.8.
- Contract C# / TypeScript / JSON Schema phải đồng bộ.

## Khi nào được dừng
Chỉ dừng khi:
- hoàn thành batch lớn có ý nghĩa,
- cần dữ liệu thực sự từ tool/user,
- gặp blocker khách quan,
- hoặc project đã DONE.

Khi còn việc có thể tự làm tiếp, tiếp tục làm.

## Final report
Khi xong chỉ trả:

DONE

BUILD: PASS / PARTIAL / BLOCKED
TESTS: PASS / PARTIAL / BLOCKED
BLOCKERS: ...
NEXT STEP: ...