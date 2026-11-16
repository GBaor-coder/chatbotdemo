"""Script seed riêng: nạp dữ liệu mẫu qua pipeline ingestion (idempotent theo content_hash).
Chạy: python scripts/seed.py  (cần .env với POSTGRES_* và X_INTERNAL_API_KEY)
Hoặc gọi qua API: POST http://ai-service:8000/ingestion/documents
"""
import psycopg2
from psycopg2.extras import RealDictCursor
from pgvector.psycopg2 import register_vector
import hashlib
import json
import os
import sys

sys.path.insert(0, os.path.join(os.path.dirname(__file__), "ai-service"))

from app.config import settings
from app.embeddings import get_model


def get_conn():
    conn = psycopg2.connect(
        dbname=settings.postgres_db,
        user=settings.postgres_user,
        password=settings.postgres_password,
        host=settings.postgres_host,
        port=settings.postgres_port,
    )
    conn.autocommit = True
    register_vector(conn)
    return conn


def content_hash(text: str) -> str:
    return hashlib.sha256(text.encode("utf-8")).hexdigest()


MAJORS = [
    ("CNTT01", "Công nghệ thông tin", "CNTT", "Đào tạo về phát triển phần mềm, hệ thống thông tin và công nghệ số.", "A00", 300),
    ("CNTT02", "Khoa học máy tính", "CNTT", "Ngành tập trung vào lý thuyết tính toán, thuật toán và trí tuệ nhân tạo.", "A00", 180),
    ("CNTT03", "Hệ thống thông tin", "CNTT", "Ngành kết hợp công nghệ thông tin và quản trị doanh nghiệp.", "A00", 150),
    ("CNTT04", "Kỹ thuật phần mềm", "CNTT", "Ngành đào tạo về quy trình phát triển phần mềm chuẩnizat.", "A00", 160),
    ("CNTT05", "An toàn thông tin", "CNTT", "Ngành về bảo mật mạng, mã hóa và an toàn thông tin.", "A00", 100),
    ("KE01", "Kinh doanh", "Kinh tế", "Ngành về quản trị doanh nghiệp, marketing và kinh doanh quốc tế.", "D01", 250),
    ("KE02", "Kinh tế", "Kinh tế", "Ngành nghiên cứu về kinh tế vĩ mô và kinh tế vi mô.", "D01", 120),
    ("KE03", "Tài chính ngân hàng", "Kinh tế", "Ngành về hoạt động tài chính, ngân hàng và đầu tư.", "D01", 140),
    ("KE04", "Kế toán", "Kinh tế", "Ngành về kế toán doanh nghiệp, kiểm toán và kiểm soát nội bộ.", "D01", 130),
    ("NG01", "Ngôn ngữ Anh", "Ngôn ngữ", "Ngành đào tạo giáo viên và chuyên viên ngôn ngữ Anh.", "D01", 100),
    ("NG02", "Ngôn ngữ Hàn Quốc", "Ngôn ngữ", "Ngành về ngôn ngữ và văn hóa Hàn Quốc.", "D01", 60),
    ("NG03", "Ngôn ngữ Nhật Bản", "Ngôn ngữ", "Ngành về ngôn ngữ và văn hóa Nhật Bản.", "D01", 60),
    ("TT01", "Truyền thông đa phương tiện", "Truyền thông", "Ngành về nội dung truyền thông số và đa phương tiện.", "D01", 80),
    ("TT02", "Quảng cáo", "Truyền thông", "Ngành về sáng tạo quảng cáo và truyền thông điệp.", "D01", 70),
]

ADMISSION_SCORES = [
    ("CNTT01", 2023, 24.50, "A00", 280), ("CNTT01", 2024, 26.00, "A00", 290), ("CNTT01", 2025, 27.50, "A00", 300),
    ("CNTT02", 2023, 23.00, "A00", 170), ("CNTT02", 2024, 24.50, "A00", 175), ("CNTT02", 2025, 26.00, "A00", 180),
    ("CNTT03", 2023, 21.00, "A00", 140), ("CNTT03", 2024, 22.50, "A00", 145), ("CNTT03", 2025, 24.00, "A00", 150),
    ("CNTT04", 2023, 22.00, "A00", 150), ("CNTT04", 2024, 23.50, "A00", 155), ("CNTT04", 2025, 25.00, "A00", 160),
    ("CNTT05", 2023, 20.00, "A00", 90),  ("CNTT05", 2024, 21.50, "A00", 95),  ("CNTT05", 2025, 23.00, "A00", 100),
    ("KE01", 2023, 19.50, "D01", 230), ("KE01", 2024, 21.00, "D01", 240), ("KE01", 2025, 22.50, "D01", 250),
    ("KE02", 2023, 18.00, "D01", 110), ("KE02", 2024, 19.50, "D01", 115), ("KE02", 2025, 21.00, "D01", 120),
    ("KE03", 2023, 19.00, "D01", 130), ("KE03", 2024, 20.50, "D01", 135), ("KE03", 2025, 22.00, "D01", 140),
    ("KE04", 2023, 17.50, "D01", 120), ("KE04", 2024, 19.00, "D01", 125), ("KE04", 2025, 20.50, "D01", 130),
    ("NG01", 2023, 17.00, "D01", 90),  ("NG01", 2024, 18.50, "D01", 95),  ("NG01", 2025, 20.00, "D01", 100),
    ("NG02", 2023, 16.00, "D01", 55),  ("NG02", 2024, 17.50, "D01", 58),  ("NG02", 2025, 19.00, "D01", 60),
    ("NG03", 2023, 16.00, "D01", 55),  ("NG03", 2024, 17.50, "D01", 58),  ("NG03", 2025, 19.00, "D01", 60),
    ("TT01", 2023, 17.50, "D01", 75),  ("TT01", 2024, 19.00, "D01", 78),  ("TT01", 2025, 20.50, "D01", 80),
    ("TT02", 2023, 17.00, "D01", 65),  ("TT02", 2024, 18.50, "D01", 68),  ("TT02", 2025, 20.00, "D01", 70),
]

TUITION_SCHOLARSHIPS = [
    ("CNTT01", 38000000, "VND", "Học bổng mérit 10-50% học phí cho học sinh thành tích."),
    ("CNTT02", 42000000, "VND", "Học bổng xuất sắc 20% học phí."),
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
    ("Xét điểm thi THPTQG", "Xét tuyển dựa trên điểm thi tốt nghiệp phổ thông quốc gia.", "Điểm thi 3 môn kết hợp với điểm ưu tiên (nếu có)."),
    ("Xét học bạ", "Xét tuyển dựa trên kết quả học tập 3 năm THPT.", "Trung bình các học kỳ từ lớp 10 đến lớp 12."),
    ("Xét kết quả học sinh giỏi", "Ưu tiên tuyển sinh cho học sinh giỏi cấp quốc gia, cấp tỉnh.", "Chứng chỉ học sinh giỏi tương ứng ngành."),
    ("Cơ sở hỗn hợp", "Kết hợp điểm thi THPTQG và điểm học bạ theo tỷ trọng quy định.", "Điểm thi THPTQG + điểm học bạ theo tỷ trọng đã công bố."),
]

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


TENANT_ID = "11111111-1111-1111-1111-111111111111"  # UNIV-A


def simple_chunk(text: str, chunk_size: int = 500) -> list[str]:
    if not text:
        return []
    words = text.split()
    chunks, current, current_len = [], [], 0
    for w in words:
        if current_len + len(w) + 1 > chunk_size and current:
            chunks.append(" ".join(current))
            current, current_len = [w], len(w)
        else:
            current.append(w)
            current_len += len(w) + 1
    if current:
        chunks.append(" ".join(current))
    return chunks


def main():
    model = get_model()
    conn = get_conn()
    try:
        with conn.cursor() as cur:
            # 1. Majors (idempotent by code)
            for code, name, block, desc, combo, quota in MAJORS:
                cur.execute(
                    """
                    INSERT INTO majors (code, name, block, description, subject_combination, quota, created_at)
                    VALUES (%s, %s, %s, %s, %s, %s, NOW())
                    ON CONFLICT (code) DO UPDATE
                    SET name = EXCLUDED.name, block = EXCLUDED.block, description = EXCLUDED.description,
                        subject_combination = EXCLUDED.subject_combination, quota = EXCLUDED.quota
                    """,
                    (code, name, block, desc, combo, quota),
                )

            # 2. Admission scores
            for code, year, score, combo, quota in ADMISSION_SCORES:
                cur.execute(
                    """
                    INSERT INTO admission_scores (major_id, year, min_score, subject_combination, quota, created_at)
                    SELECT id, %s, %s, %s, %s, NOW() FROM majors WHERE code = %s
                    ON CONFLICT (major_id, year) DO UPDATE
                    SET min_score = EXCLUDED.min_score, subject_combination = EXCLUDED.subject_combination, quota = EXCLUDED.quota
                    """,
                    (year, score, combo, quota, code),
                )

            # 3. Tuition scholarships
            for code, tuition, currency, info in TUITION_SCHOLARSHIPS:
                cur.execute(
                    """
                    INSERT INTO tuition_scholarships (major_id, tuition_per_year, currency, scholarship_info, created_at)
                    SELECT id, %s, %s, %s, NOW() FROM majors WHERE code = %s
                    ON CONFLICT (major_id) DO UPDATE
                    SET tuition_per_year = EXCLUDED.tuition_per_year, currency = EXCLUDED.currency, scholarship_info = EXCLUDED.scholarship_info
                    """,
                    (tuition, currency, info, code),
                )

            # 4. Admission methods
            for method_name, desc, req in ADMISSION_METHODS:
                cur.execute(
                    """
                    INSERT INTO admission_methods (method_name, description, requirements, created_at)
                    VALUES (%s, %s, %s, NOW())
                    ON CONFLICT DO NOTHING
                    """,
                    (method_name, desc, req),
                )

            # 5. Documents + chunks (idempotent by content_hash)
            for chunk in KNOWLEDGE_CHUNKS:
                embedding = model.encode(chunk["content"], normalize_embeddings=True)
                emb_list = [float(x) for x in embedding]
                chash = content_hash(chunk["content"])

                # Upsert document
                cur.execute(
                    """
                    INSERT INTO documents (tenant_id, source, title, doc_type, status, content_hash, metadata, created_at, updated_at)
                    VALUES (%s::uuid, %s, %s, 'text', 'published', %s, %s::jsonb, NOW(), NOW())
                    ON CONFLICT (tenant_id, source, title) DO UPDATE
                    SET content_hash = EXCLUDED.content_hash, metadata = EXCLUDED.metadata, updated_at = NOW()
                    RETURNING id
                    """,
                    (TENANT_ID, chunk["source"], chunk["title"], chash, json.dumps(chunk["metadata"], ensure_ascii=False)),
                )
                doc_row = cur.fetchone()
                doc_id = str(doc_row[0]) if doc_row else None

                if doc_id:
                    # Delete old chunks for this document (re-embed idempotent)
                    cur.execute("DELETE FROM knowledge_chunks WHERE document_id = %s::uuid", (doc_id,))
                    # Insert new chunks
                    chunks = simple_chunk(chunk["content"])
                    for i, chunk_text in enumerate(chunks):
                        chunk_emb = model.encode(chunk_text, normalize_embeddings=True)
                        chunk_emb_list = [float(x) for x in chunk_emb]
                        chunk_hash = content_hash(chunk_text)
                        cur.execute(
                            """
                            INSERT INTO knowledge_chunks
                                (tenant_id, document_id, chunk_index, content, embedding, content_hash,
                                 embedding_model, embedding_version, lang, metadata, created_at)
                            VALUES (%s::uuid, %s::uuid, %s, %s, %s::vector, %s, %s, %s, %s, %s::jsonb, NOW())
                            """,
                            (
                                TENANT_ID, doc_id, i, chunk_text, chunk_emb_list, chunk_hash,
                                settings.embedding_model, "1", "vi",
                                json.dumps(chunk["metadata"], ensure_ascii=False),
                            ),
                        )

        conn.commit()
        print("Seed data loaded successfully (idempotent by content_hash).")
    finally:
        conn.close()


if __name__ == "__main__":
    main()
