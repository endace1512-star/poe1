# poe1

Công cụ all-in-one cho **Path of Exile 1**, chạy hoàn toàn trên máy của bạn (http://127.0.0.1:8787).
Không phải đăng nhập tài khoản vào trang web của bên thứ ba: POESESSID chỉ nằm trên máy bạn và chỉ được gửi tới pathofexile.com.

## Tính năng

| Tab | Làm gì |
|---|---|
| **Tổng quan** | Net worth theo thời gian (div/chaos), giá trị theo tab và nhân vật, top item |
| **Theo tab** | Item của từng tab stash / nhân vật, có đơn giá và tổng |
| **Item** | Mọi item gộp lại, lọc theo loại, tên, giá trị |
| **Cảnh báo giá** | Món đang giữ đổi giá ≥ X% so với 1 giờ / 6 giờ / hôm qua / tuần trước, kèm thông báo desktop sau mỗi snapshot |
| **Phiên farm** | Bắt đầu / kết thúc phiên, tính loot thu được, chaos/giờ, món đã dùng |
| **Leveling** | Lộ trình Act 1–10 từng bước, **ảnh layout của từng khu vực**, tự chuyển bước khi bạn đổi khu vực (đọc `Client.txt`), nhập build Path of Building để biết gem nào lấy từ quest, gem nào mua ở NPC |
| **So build** | Dán link pobb.in / pastebin / poe.ninja hoặc mã PoB: tool đoán bạn đang ở giai đoạn nào của build (theo cây passive và level), so từng món đồ, gem, jewel, cây passive với build; tick **Bỏ qua** cho món đã có đồ tương tự hoặc không mua/craft được |
| Timeless, Flip, Regex, Patch game | Sắp có (xem *Kế hoạch*) |

Giá lấy từ [poe.ninja](https://poe.ninja): currency, fragment, div card, essence, thread/scarab, unique (theo số link),
gem (theo level/quality/corrupt), base item (theo ilvl và influence), cluster jewel, map…

### So build hoạt động thế nào

- Đồ của nhân vật đọc từ pathofexile.com (cần POESESSID như phần Net worth). Game lưu nhân vật lên server khi bạn đổi khu vực,
  nên khi đang chơi, tab tự làm mới mỗi lần bạn qua map (nhờ `Client.txt`, tối đa 1 lần/phút).
- **Giai đoạn**: build hướng dẫn thường có nhiều cây passive / bộ đồ / bộ gem (Level 40, Level 70, Endgame…).
  Tool chọn cây giống cây của bạn nhất; nếu không đọc được cây thì dựa vào level trong tên giai đoạn. Bạn chọn lại tay được, và có nút *Giai đoạn tiếp*.
- **Đồ**: unique so theo tên; rare/magic so từng dòng mod (đủ / thấp hơn / thiếu) và tổng Life, kháng, thuộc tính; kèm số link.
  Nhẫn và flask được ghép cặp tự động dù bạn đeo khác ô.
- **Gem**: có / level thấp / ở chỗ khác / thiếu, và có nằm chung một nhóm link không.
- **Cây passive**: số điểm khớp, notable/keystone còn thiếu và điểm đang có mà build không lấy (cần respec). Tên node lấy từ dữ liệu cây chính thức của GGG.

## Cài và chạy (Windows)

1. Tải `poe1.exe` ở trang [Releases](https://github.com/endace1512-star/poe1/releases) (hoặc từ artifact của workflow *Release*).
2. Chạy `poe1.exe`. Trình duyệt tự mở http://127.0.0.1:8787. Không cần cài .NET.
3. Vào **Cài đặt**:
   - **Tên tài khoản**: ghi đủ cả phần `#1234`.
   - **POESESSID**: đăng nhập pathofexile.com → F12 → *Application* → *Cookies* → `POESESSID`.
   - **League**, rồi bấm **Lưu cài đặt** và **Chụp snapshot**.
4. Để dùng Leveling tự chuyển bước: để trống ô *Client.txt* (tự tìm ở thư mục cài mặc định) hoặc nhập đường dẫn `...\Path of Exile\logs\Client.txt`.

Snapshot tự chạy mỗi 15 phút (đổi được). Dữ liệu và cài đặt lưu ở `%LOCALAPPDATA%\poe1\`
(`settings.json`, `poe1.db`, cache giá). POESESSID được mã hoá bằng Windows DPAPI.

Tham số dòng lệnh: `--port 8787`, `--no-browser`, `--demo` (dữ liệu mẫu, không cần tài khoản).

## Phát triển

Cần [.NET 10 SDK](https://dotnet.microsoft.com/download) và Node.js 22.

```bash
cd web && npm install && npm run build   # build UI vào src/Poe1.Server/wwwroot
cd .. && dotnet run --project src/Poe1.Server -- --demo
dotnet test
```

Sửa giao diện có hot reload: chạy server như trên, rồi `cd web && npm run dev` và mở http://localhost:5173.

Build exe như bản release:

```bash
dotnet publish src/Poe1.Server -c Release -r win-x64 --self-contained -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -o publish
```

### Cấu trúc

```
src/Poe1.Server/        ASP.NET Core (.NET 10), API + phục vụ UI
  Poe/                  gọi pathofexile.com (stash, nhân vật) + rate limit theo header của GGG
  Pricing/              poe.ninja (API mới /poe1/api/economy/...) + so khớp item → giá
  Wealth/               snapshot, SQLite, cảnh báo giá, phiên farm
  Leveling/             đọc Client.txt → sự kiện đổi khu vực / lên level
web/                    React + Vite + TypeScript
  src/pages/            các tab
  src/leveling/         trang Leveling, engine lộ trình (từ exile-leveling)
  public/layouts/       ảnh layout khu vực (từ Exile-UI)
tests/Poe1.Tests/       xUnit
```

## Kế hoạch

- **Patch game** (kiểu PoeRedux): tắt particle/fog/shadow/corpse, camera zoom, minimap, màu map mod; có sao lưu và khôi phục.
- **Regex** (kiểu poe.re): chuỗi tìm kiếm cho map mod, vendor, stash.
- **Timeless jewel finder** chạy offline.
- **Flip**: chênh lệch tỉ giá currency exchange.
- Về sau: tích hợp Path of Building, price check kiểu Awakened PoE Trade, overlay trong game.

## Ghi công

- Lộ trình leveling và dữ liệu khu vực/quest/gem: [exile-leveling](https://github.com/HeartofPhos/exile-leveling) (MIT).
- Ảnh layout khu vực: [Exile-UI](https://github.com/Lailloken/Exile-UI) (MIT).
- Giá: [poe.ninja](https://poe.ninja).
- Cách đọc build PoB: theo định dạng của [Path of Building Community](https://github.com/PathOfBuildingCommunity/PathOfBuilding) (MIT); build mẫu dùng trong test lấy từ repo này.
- Tên node cây passive: [grindinggear/skilltree-export](https://github.com/grindinggear/skilltree-export) (tải lúc chạy).

Chi tiết giấy phép ở [THIRD_PARTY_NOTICES.md](THIRD_PARTY_NOTICES.md).
Path of Exile là thương hiệu của Grinding Gear Games; dự án này không liên kết với GGG.
