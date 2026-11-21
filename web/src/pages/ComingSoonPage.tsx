const PLANS: Record<string, { title: string; text: string; items: string[] }> = {
  timeless: {
    title: "Timeless jewel finder",
    text: "Tìm seed Timeless Jewel theo mod mong muốn, chạy hoàn toàn trên máy (không cần trang ngoài).",
    items: [
      "Dữ liệu seed lấy từ Path of Building (giấy phép MIT)",
      "Chọn loại jewel, socket trên cây, mod cần có → danh sách seed xếp theo điểm",
      "Tạo link tìm trên trade cho các seed tốt",
    ],
  },
  flip: {
    title: "Flip (beta)",
    text: "Gợi ý cặp đổi currency có lời dựa trên tỉ giá currency exchange.",
    items: ["Theo dõi chênh lệch mua/bán theo thời gian", "Lọc theo khối lượng giao dịch tối thiểu"],
  },
  regex: {
    title: "Regex (kiểu poe.re)",
    text: "Tạo chuỗi tìm kiếm regex cho stash, vendor và map mod.",
    items: [
      "Map mod: chọn mod muốn tránh / cần có, giới hạn 250 ký tự của game",
      "Vendor: link, màu socket, tốc độ chạy…",
      "Lưu preset hay dùng",
    ],
  },
  patch: {
    title: "Patch game (kiểu PoeRedux)",
    text: "Bật/tắt hiệu ứng để game mượt hơn bằng cách sửa file game, có sao lưu và khôi phục bản gốc.",
    items: [
      "Dựa trên PoeRedux (giấy phép MIT): tắt particle, fog, shadow, corpse, delirium fog…",
      "Camera zoom, minimap mở sẵn, màu cho map mod nguy hiểm",
      "Chỉ chạy trên Windows; luôn đóng game trước khi áp dụng",
    ],
  },
};

export function ComingSoonPage({ page }: { page: string }) {
  const plan = PLANS[page];
  return (
    <div className="card">
      <h2>{plan.title}</h2>
      <p className="muted">{plan.text}</p>
      <ul className="plan-list">
        {plan.items.map((i) => (
          <li key={i}>{i}</li>
        ))}
      </ul>
      <p className="faint small" style={{ marginTop: 16 }}>
        Tính năng này đang được làm.
      </p>
    </div>
  );
}
