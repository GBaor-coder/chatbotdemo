from .database import (
    get_conn,
    upsert_knowledge_chunk,
    save_message,
    get_or_create_session,
)
from .embeddings import embed_text
import json


# ---------------------------------------------------------------------------
# Dữ liệu tuyển sinh Đại học Việt Nam giả lập chi tiết (2023-2025)
# ---------------------------------------------------------------------------

MAJORS = [
    {
        "code": "CNTT01",
        "name": "Công nghệ thông tin",
        "block": "CNTT",
        "description": "Đào tạo về phát triển phần mềm, hệ thống thông tin, mạng máy tính và công nghệ số.",
        "subject_combination": "A00 (Toán, Lý, Anh)",
        "quota": 300,
    },
    {
        "code": "CNTT02",
        "name": "Khoa học máy tính",
        "block": "CNTT",
        "description": "Đào tạo về lý thuyết tính toán, thuật toán, trí tuệ nhân tạo và khoa học dữ liệu.",
        "subject_combination": "A00 (Toán, Lý, Anh)",
        "quota": 180,
    },
    {
        "code": "CNTT03",
        "name": "Hệ thống thông tin",
        "block": "CNTT",
        "description": "Kết hợp công nghệ thông tin với quản trị doanh nghiệp, phân tích hệ thống.",
        "subject_combination": "A00 (Toán, Lý, Anh)",
        "quota": 150,
    },
    {
        "code": "CNTT04",
        "name": "Kỹ thuật phần mềm",
        "block": "CNTT",
        "description": "Đào tạo về quy trình phát triển phần mềm, kiểm thử và bảo trì phần mềm.",
        "subject_combination": "A00 (Toán, Lý, Anh)",
        "quota": 160,
    },
    {
        "code": "CNTT05",
        "name": "An toàn thông tin",
        "block": "CNTT",
        "description": "Đào tạo về bảo mật mạng, mã hóa, an toàn thông tin và chứng thực an toàn mạng.",
        "subject_combination": "A00 (Toán, Lý, Anh)",
        "quota": 100,
    },
    {
        "code": "KE01",
        "name": "Kinh doanh",
        "block": "Kinh tế",
        "description": "Đào tạo về quản trị doanh nghiệp, marketing, kinh doanh quốc tế và khởi nghiệp.",
        "subject_combination": "D01 (Toán, Văn, Anh)",
        "quota": 250,
    },
    {
        "code": "KE02",
        "name": "Kinh tế",
        "block": "Kinh tế",
        "description": "Nghiên cứu về kinh tế vĩ mô, vi mô, phát triển kinh tế và chính sách công.",
        "subject_combination": "D01 (Toán, Văn, Anh)",
        "quota": 120,
    },
    {
        "code": "KE03",
        "name": "Tài chính ngân hàng",
        "block": "Kinh tế",
        "description": "Đào tạo về hoạt động tài chính doanh nghiệp, ngân hàng, chứng khoán và đầu tư.",
        "subject_combination": "D01 (Toán, Văn, Anh)",
        "quota": 140,
    },
    {
        "code": "KE04",
        "name": "Kế toán",
        "block": "Kinh tế",
        "description": "Đào tạo về kế toán doanh nghiệp, kiểm toán, kiểm soát nội bộ và thuế.",
        "subject_combination": "D01 (Toán, Văn, Anh)",
        "quota": 130,
    },
    {
        "code": "NG01",
        "name": "Ngôn ngữ Anh",
        "block": "Ngôn ngữ",
        "description": "Đào tạo giáo viên và chuyên viên ngôn ngữ Anh, dịch thuật và giao dịch quốc tế.",
        "subject_combination": "D01 (Toán, Văn, Anh)",
        "quota": 100,
    },
    {
        "code": "NG02",
        "name": "Ngôn ngữ Hàn Quốc",
        "block": "Ngôn ngữ",
        "description": "Đào tạo về ngôn ngữ, văn hóa Hàn Quốc và dịch thuật Hàn-Việt.",
        "subject_combination": "D01 (Toán, Văn, Anh)",
        "quota": 60,
    },
    {
        "code": "NG03",
        "name": "Ngôn ngữ Nhật Bản",
        "block": "Ngôn ngữ",
        "description": "Đào tạo về ngôn ngữ, văn hóa Nhật Bản và dịch thuật Nhật-Việt.",
        "subject_combination": "D01 (Toán, Văn, Anh)",
        "quota": 60,
    },
    {
        "code": "TT01",
        "name": "Truyền thông đa phương tiện",
        "block": "Truyền thông",
        "description": "Đào tạo về nội dung truyền thông số, hình ảnh, âm thanh và đa phương tiện.",
        "subject_combination": "D01 (Toán, Văn, Anh)",
        "quota": 80,
    },
    {
        "code": "TT02",
        "name": "Quảng cáo",
        "block": "Truyền thông",
        "description": "Đào tạo về sáng tạo quảng cáo, truyền播 thông điệp và quản trị thương hiệu.",
        "subject_combination": "D01 (Toán, Văn, Anh)",
        "quota": 70,
    },
]

ADMISSION_SCORES = [
    # CNTT01
    ("CNTT01", 2023, 24.50, "A00", 280),
    ("CNTT01", 2024, 26.00, "A00", 290),
    ("CNTT01", 2025, 27.50, "A00", 300),
    # CNTT02
    ("CNTT02", 2023, 23.00, "A00", 170),
    ("CNTT02", 2024, 24.50, "A00", 175),
    ("CNTT02", 2025, 26.00, "A00", 180),
    # CNTT03
    ("CNTT03", 2023, 21.00, "A00", 140),
    ("CNTT03", 2024, 22.50, "A00", 145),
    ("CNTT03", 2025, 24.00, "A00", 150),
    # CNTT04
    ("CNTT04", 2023, 22.00, "A00", 150),
    ("CNTT04", 2024, 23.50, "A00", 155),
    ("CNTT04", 2025, 25.00, "A00", 160),
    # CNTT05
    ("CNTT05", 2023, 20.00, "A00", 90),
    ("CNTT05", 2024, 21.50, "A00", 95),
    ("CNTT05", 2025, 23.00, "A00", 100),
    # KE01
    ("KE01", 2023, 19.50, "D01", 230),
    ("KE01", 2024, 21.00, "D01", 240),
    ("KE01", 2025, 22.50, "D01", 250),
    # KE02
    ("KE02", 2023, 18.00, "D01", 110),
    ("KE02", 2024, 19.50, "D01", 115),
    ("KE02", 2025, 21.00, "D01", 120),
    # KE03
    ("KE03", 2023, 19.00, "D01", 130),
    ("KE03", 2024, 20.50, "D01", 135),
    ("KE03", 2025, 22.00, "D01", 140),
    # KE04
    ("KE04", 2023, 17.50, "D01", 120),
    ("KE04", 2024, 19.00, "D01", 125),
    ("KE04", 2025, 20.50, "D01", 130),
    # NG01
    ("NG01", 2023, 17.00, "D01", 90),
    ("NG01", 2024, 18.50, "D01", 95),
    ("NG01", 2025, 20.00, "D01", 100),
    # NG02
    ("NG02", 2023, 16.00, "D01", 55),
    ("NG02", 2024, 17.50, "D01", 58),
    ("NG02", 2025, 19.00, "D01", 60),
    # NG03
    ("NG03", 2023, 16.00, "D01", 55),
    ("NG03", 2024, 17.50, "D01", 58),
    ("NG03", 2025, 19.00, "D01", 60),
    # TT01
    ("TT01", 2023, 17.50, "D01", 75),
    ("TT01", 2024, 19.00, "D01", 78),
    ("TT01", 2025, 20.50, "D01", 80),
    # TT02
    ("TT02", 2023, 17.00, "D01", 65),
    ("TT02", 2024, 18.50, "D01", 68),
    ("TT02", 2025, 20.00, "D01", 70),
]

TUITION_SCHOLARSHIPS = [
    ("CNTT01", 38000000, "VND", "Học bổng mérite 10-50% học phí cho học sinh thành tích; miễn giảm học phí đối với người có công với cách mạng."),
    ("CNTT02", 42000000, "VND", "Học bổng xuất sắc 20% học phí; hỗ trợ thực tập tại các công ty công nghệ lớn."),
    ("CNTT03", 36000000, "VND", "Học bổng khuyến khích 15% học phí cho điểm thi cao."),
    ("CNTT04", 37000000, "VND", "Học bổng 10% học phí; hỗ trợ du học hợp tác quốc tế."),
    ("CNTT05", 40000000, "VND", "Học bổng an toàn thông tin 25% học phí cho sinh viên giỏi."),
    ("KE01", 32000000, "VND", "Học bổng khuyến khích 15% học phí; hỗ trợ thực tế doanh nghiệp."),
    ("KE02", 28000000, "VND", "Học bổng nghiên cứu kinh tế 10% học phí."),
    ("KE03", 30000000, "VND", "Học bổng tài chính ngân hàng 20% học phí; hỗ trợ làm việc tại ngân hàng."),
    ("KE04", 29000000, "VND", "Học bổng kế toán 10% học phí; hỗ trợ chứng chỉ kế toán."),
    ("NG01", 25000000, "VND", "Học bổng ngoại ngữ 15% học phí; hỗ trợ du học Anh, Mỹ."),
    ("NG02", 24000000, "VND", "Học bổng Hàn Quốc 20% học phí; hỗ trợ trao đổi sinh viên tại Hàn Quốc."),
    ("NG03", 24000000, "VND", "Học bổng Nhật Bản 20% học phí; hỗ trợ trao đổi sinh viên tại Nhật Bản."),
    ("TT01", 30000000, "VND", "Học bổng truyền thông 12% học phí; hỗ trợ thực tập tại các kênh truyền hình."),
    ("TT02", 28000000, "VND", "Học bổng quảng cáo 10% học phí; hỗ trợ thực tập tại các công ty quảng cáo."),
]

ADMISSION_METHODS = [
    {
        "method_name": "Xét điểm thi THPTQG",
        "description": "Xét tuyển dựa trên điểm thi tốt nghiệp phổ thông quốc gia.",
        "requirements": "Điểm thi 3 môn kết hợp với điểm ưu tiên (nếu có).",
    },
    {
        "method_name": "Xét học bạ",
        "description": "Xét tuyển dựa trên kết quả học tập 3 năm trung học phổ thông.",
        "requirements": "Trung bình các học kỳ từ lớp 10 đến lớp 12 đạt mức quy định.",
    },
    {
        "method_name": "Xét kết quả học sinh giỏi",
        "description": "Ưu tiên tuyển sinh cho học sinh giỏi cấp quốc gia, cấp tỉnh.",
        "requirements": "Chứng chỉ học sinh giỏi tương ứng ngành đăng ký.",
    },
    {
        "method_name": "Cơ sở hỗn hợp",
        "description": "Kết hợp điểm thi THPTQG và điểm học bạ theo tỷ trọng quy định.",
        "requirements": "Điểm thi THPTQG + điểm học bạ theo tỷ trọng đã công bố.",
    },
]

# ---------------------------------------------------------------------------
# Knowledge chunks cho RAG (được embed và lưu vào pgvector)
# ---------------------------------------------------------------------------

KNOWLEDGE_CHUNKS = [
    {
        "source": "majors",
        "title": "Danh sách ngành tuyển sinh 2026",
        "content": "Các ngành tuyển sinh bao gồm: Công nghệ thông tin, Khoa học máy tính, Hệ thống thông tin, Kỹ thuật phần mềm, An toàn thông tin (khối CNTT); Kinh doanh, Kinh tế, Tài chính ngân hàng, Kế toán (khối Kinh tế); Ngôn ngữ Anh, Ngôn ngữ Hàn Quốc, Ngôn ngữ Nhật Bản (khối Ngôn ngữ); Truyền thông đa phương tiện, Quảng cáo (khối Truyền thông).",
        "metadata": {"category": "majors_list", "year": 2026},
    },
    {
        "source": "admission_scores",
        "title": "Điểm chuẩn tuyển sinh 2023-2025",
        "content": "Điểm chuẩn các ngành CNTT: Công nghệ thông tin (2023: 24.5, 2024: 26.0, 2025: 27.5), Khoa học máy tính (2023: 23.0, 2024: 24.5, 2025: 26.0), Hệ thống thông tin (2023: 21.0, 2024: 22.5, 2025: 24.0), Kỹ thuật phần mềm (2023: 22.0, 2024: 23.5, 2025: 25.0), An toàn thông tin (2023: 20.0, 2024: 21.5, 2025: 23.0). Các ngành Kinh tế: Kinh doanh (2023: 19.5, 2024: 21.0, 2025: 22.5), Kinh tế (2023: 18.0, 2024: 19.5, 2025: 21.0), Tài chính ngân hàng (2023: 19.0, 2024: 20.5, 2025: 22.0), Kế toán (2023: 17.5, 2024: 19.0, 2025: 20.5). Các ngành Ngôn ngữ: Ngôn ngữ Anh (2023: 17.0, 2024: 18.5, 2025: 20.0), Ngôn ngữ Hàn Quốc (2023: 16.0, 2024: 17.5, 2025: 19.0), Ngôn ngữ Nhật Bản (2023: 16.0, 2024: 17.5, 2025: 19.0). Các ngành Truyền thông: Truyền thông đa phương tiện (2023: 17.5, 2024: 19.0, 2025: 20.5), Quảng cáo (2023: 17.0, 2024: 18.5, 2025: 20.0).",
        "metadata": {"category": "admission_scores", "years": [2023, 2024, 2025]},
    },
    {
        "source": "tuition_scholarships",
        "title": "Học phí và học bổng các ngành",
        "content": "Học phí mỗi năm (VND): Công nghệ thông tin 38 triệu, Khoa học máy tính 42 triệu, Hệ thống thông tin 36 triệu, Kỹ thuật phần mềm 37 triệu, An toàn thông tin 40 triệu, Kinh doanh 32 triệu, Kinh tế 28 triệu, Tài chính ngân hàng 30 triệu, Kế toán 29 triệu, Ngôn ngữ Anh 25 triệu, Ngôn ngữ Hàn Quốc 24 triệu, Ngôn ngữ Nhật Bản 24 triệu, Truyền thông đa phương tiện 30 triệu, Quảng cáo 28 triệu. Học bổng: 10-50% học phí cho học sinh thành tích, hỗ trợ du học, thực tập doanh nghiệp.",
        "metadata": {"category": "tuition", "currency": "VND"},
    },
    {
        "source": "admission_methods",
        "title": "Phương thức xét tuyển 2026",
        "content": "Các phương thức xét tuyển: Xét điểm thi THPTQG (điểm thi 3 môn + ưu tiên), Xét học bạ (trung bình 3 năm THPT), Xét kết quả học sinh giỏi (chứng chỉ cấp quốc gia/tỉnh), Cơ sở hỗn hợp (kết hợp điểm thi và học bạ theo tỷ trọng).",
        "metadata": {"category": "admission_methods"},
    },
    {
        "source": "schedule",
        "title": "Lịch trình tuyển sinh 2026",
        "content": "Lịch trình dự kiến: Đăng ký xét tuyển tháng 5-6/2026, thi THPTQG tháng 6/2026, công bố kết quả tháng 7/2026, nhập học tháng 8-9/2026. Các ngày chi tiết sẽ được công bố trên trang web nhà trường.",
        "metadata": {"category": "schedule", "year": 2026},
    },
]


def seed():
    """Nạp toàn bộ dữ liệu tuyển sinh vào PostgreSQL và embed vào pgvector."""
    conn = get_conn()
    try:
        # 1. Insert majors
        with conn.cursor() as cur:
            for m in MAJORS:
                cur.execute(
                    """
                    INSERT INTO majors (code, name, block, description, subject_combination, quota, created_at)
                    VALUES (%s, %s, %s, %s, %s, %s, NOW())
                    ON CONFLICT (code) DO UPDATE
                    SET name = EXCLUDED.name,
                        block = EXCLUDED.block,
                        description = EXCLUDED.description,
                        subject_combination = EXCLUDED.subject_combination,
                        quota = EXCLUDED.quota
                    """,
                    (
                        m["code"],
                        m["name"],
                        m["block"],
                        m["description"],
                        m["subject_combination"],
                        m["quota"],
                    ),
                )

        # 2. Insert admission_scores
        with conn.cursor() as cur:
            for code, year, score, combo, quota in ADMISSION_SCORES:
                cur.execute(
                    """
                    INSERT INTO admission_scores (major_id, year, min_score, subject_combination, quota, created_at)
                    SELECT id, %s, %s, %s, %s, NOW() FROM majors WHERE code = %s
                    ON CONFLICT DO NOTHING
                    """,
                    (year, score, combo, quota, code),
                )

        # 3. Insert tuition_scholarships
        with conn.cursor() as cur:
            for code, tuition, currency, info in TUITION_SCHOLARSHIPS:
                cur.execute(
                    """
                    INSERT INTO tuition_scholarships (major_id, tuition_per_year, currency, scholarship_info, created_at)
                    SELECT id, %s, %s, %s, NOW() FROM majors WHERE code = %s
                    ON CONFLICT DO NOTHING
                    """,
                    (tuition, currency, info, code),
                )

        # 4. Insert admission_methods
        with conn.cursor() as cur:
            for m in ADMISSION_METHODS:
                cur.execute(
                    """
                    INSERT INTO admission_methods (method_name, description, requirements, created_at)
                    VALUES (%s, %s, %s, NOW())
                    ON CONFLICT DO NOTHING
                    """,
                    (m["method_name"], m["description"], m["requirements"]),
                )

        conn.commit()

        # 5. Embed and upsert knowledge_chunks
        with conn.cursor() as cur:
            for chunk in KNOWLEDGE_CHUNKS:
                embedding = embed_text(chunk["content"])
                upsert_knowledge_chunk(
                    source=chunk["source"],
                    title=chunk["title"],
                    content=chunk["content"],
                    embedding=embedding,
                    metadata=chunk["metadata"],
                )
        conn.commit()
    finally:
        conn.close()


if __name__ == "__main__":
    seed()
    print("Seed data loaded and embedded into pgvector.")
