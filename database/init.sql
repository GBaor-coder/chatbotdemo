-- PostgreSQL 16 + pgvector schema
-- Vector dimension: 1024 (BAAI/bge-m3 dense output)

CREATE EXTENSION IF NOT EXISTS vector;

-- ============================================================
-- Bảng nghiệp vụ: Ngành học
-- ============================================================
CREATE TABLE IF NOT EXISTS majors (
    id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    code VARCHAR(20) NOT NULL UNIQUE,
    name VARCHAR(255) NOT NULL,
    block VARCHAR(50) NOT NULL,
    description TEXT,
    subject_combination VARCHAR(50),
    quota INTEGER,
    created_at TIMESTAMPTZ NOT NULL DEFAULT NOW()
);

-- ============================================================
-- Bảng nghiệp vụ: Điểm chuẩn theo năm
-- ============================================================
CREATE TABLE IF NOT EXISTS admission_scores (
    id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    major_id UUID NOT NULL REFERENCES majors(id) ON DELETE CASCADE,
    year SMALLINT NOT NULL,
    min_score NUMERIC(4,2) NOT NULL,
    subject_combination VARCHAR(50) NOT NULL,
    quota INTEGER,
    created_at TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    UNIQUE (major_id, year)
);

-- ============================================================
-- Bảng nghiệp vụ: Học phí và học bổng
-- ============================================================
CREATE TABLE IF NOT EXISTS tuition_scholarships (
    id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    major_id UUID NOT NULL REFERENCES majors(id) ON DELETE CASCADE,
    tuition_per_year NUMERIC(12,2) NOT NULL,
    currency VARCHAR(10) NOT NULL DEFAULT 'VND',
    scholarship_info TEXT,
    created_at TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    UNIQUE (major_id)
);

-- ============================================================
-- Bảng nghiệp vụ: Phương thức xét tuyển
-- ============================================================
CREATE TABLE IF NOT EXISTS admission_methods (
    id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    method_name VARCHAR(100) NOT NULL,
    description TEXT,
    requirements TEXT,
    created_at TIMESTAMPTZ NOT NULL DEFAULT NOW()
);

-- ============================================================
-- Bảng nghiệp vụ: Phiên trò chuyện
-- ============================================================
CREATE TABLE IF NOT EXISTS chat_sessions (
    id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    user_id VARCHAR(100) NOT NULL,
    title VARCHAR(255) NOT NULL DEFAULT 'Cuộc trò chuyện mới',
    created_at TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    updated_at TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    deleted_at TIMESTAMPTZ
);

-- ============================================================
-- Bảng nghiệp vụ: Tin nhắn trong phiên
-- ============================================================
CREATE TABLE IF NOT EXISTS chat_messages (
    id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    session_id UUID NOT NULL REFERENCES chat_sessions(id) ON DELETE CASCADE,
    role VARCHAR(20) NOT NULL CHECK (role IN ('user','assistant','system')),
    content TEXT NOT NULL,
    metadata JSONB DEFAULT '{}'::jsonb,
    created_at TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    deleted_at TIMESTAMPTZ
);

-- ============================================================
-- Bảng vector: Lưu trữ知识 chunks kèm embedding
-- ============================================================
CREATE TABLE IF NOT EXISTS knowledge_chunks (
    id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    source VARCHAR(255) NOT NULL,
    title VARCHAR(255) NOT NULL,
    content TEXT NOT NULL,
    embedding vector(1024),
    metadata JSONB DEFAULT '{}'::jsonb,
    created_at TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    UNIQUE (source, title)
);

-- ============================================================
-- Chỉ số
-- ============================================================
CREATE INDEX IF NOT EXISTS idx_admission_scores_major_year ON admission_scores(major_id, year);
CREATE INDEX IF NOT EXISTS idx_tuition_scholarships_major ON tuition_scholarships(major_id);
CREATE INDEX IF NOT EXISTS idx_chat_sessions_user ON chat_sessions(user_id, created_at);
CREATE INDEX IF NOT EXISTS idx_chat_messages_session ON chat_messages(session_id, created_at);
CREATE INDEX IF NOT EXISTS idx_knowledge_embedding ON knowledge_chunks USING hnsw (embedding vector_cosine_ops);

-- ============================================================
-- Dữ liệu mẫu khởi tạo (seed dữ liệu tuyển sinh Việt Nam)
-- ============================================================
INSERT INTO majors (code, name, block, description) VALUES
('CNTT01', 'Công nghệ thông tin', 'CNTT', 'Ngành đào tạo về phát triển phần mềm, hệ thống thông tin và công nghệ số.'),
('CNTT02', 'Khoa học máy tính', 'CNTT', 'Ngành tập trung vào lý thuyết tính toán, thuật toán và trí tuệ nhân tạo.'),
('CNTT03', 'Hệ thống thông tin', 'CNTT', 'Ngành kết hợp công nghệ thông tin và quản trị doanh nghiệp.'),
('CNTT04', 'Kỹ thuật phần mềm', 'CNTT', 'Ngành đào tạo về quy trình phát triển phần mềm chuẩnizat.'),
('CNTT05', 'An toàn thông tin', 'CNTT', 'Ngành về bảo mật mạng, mã hóa và an toàn thông tin.'),
('KE01', 'Kinh doanh', 'Kinh tế', 'Ngành về quản trị doanh nghiệp, marketing và kinh doanh quốc tế.'),
('KE02', 'Kinh tế', 'Kinh tế', 'Ngành nghiên cứu về kinh tế vĩ mô và kinh tế vi mô.'),
('KE03', 'Tài chính ngân hàng', 'Kinh tế', 'Ngành về hoạt động tài chính, ngân hàng và đầu tư.'),
('KE04', 'Kế toán', 'Kinh tế', 'Ngành về kế toán doanh nghiệp, kiểm toán và kiểm soát nội bộ.'),
('NG01', 'Ngôn ngữ Anh', 'Ngôn ngữ', 'Ngành đào tạo giáo viên và chuyên viên ngôn ngữ Anh.'),
('NG02', 'Ngôn ngữ Hàn Quốc', 'Ngôn ngữ', 'Ngành về ngôn ngữ và văn hóa Hàn Quốc.'),
('NG03', 'Ngôn ngữ Nhật Bản', 'Ngôn ngữ', 'Ngành về ngôn ngữ và văn hóa Nhật Bản.'),
('TT01', 'Truyền thông đa phương tiện', 'Truyền thông', 'Ngành về nội dung truyền thông số và đa phương tiện.'),
('TT02', 'Quảng cáo', 'Truyền thông', 'Ngành về sáng tạo quảng cáo và truyền播 thông điệp.')
ON CONFLICT (code) DO NOTHING;

INSERT INTO admission_scores (major_id, year, min_score, subject_combination, quota)
SELECT m.id, 2023, 24.0, 'A00', 150 FROM majors m WHERE m.code = 'CNTT01'
UNION ALL SELECT m.id, 2024, 25.5, 'A00', 160 FROM majors m WHERE m.code = 'CNTT01'
UNION ALL SELECT m.id, 2025, 27.0, 'A00', 170 FROM majors m WHERE m.code = 'CNTT01'
UNION ALL SELECT m.id, 2023, 22.5, 'A00', 120 FROM majors m WHERE m.code = 'CNTT02'
UNION ALL SELECT m.id, 2024, 24.0, 'A00', 130 FROM majors m WHERE m.code = 'CNTT02'
UNION ALL SELECT m.id, 2025, 26.0, 'A00', 140 FROM majors m WHERE m.code = 'CNTT02'
UNION ALL SELECT m.id, 2023, 20.0, 'D01', 100 FROM majors m WHERE m.code = 'KE01'
UNION ALL SELECT m.id, 2024, 21.5, 'D01', 110 FROM majors m WHERE m.code = 'KE01'
UNION ALL SELECT m.id, 2025, 23.0, 'D01', 120 FROM majors m WHERE m.code = 'KE01'
UNION ALL SELECT m.id, 2023, 18.0, 'D01', 80 FROM majors m WHERE m.code = 'NG01'
UNION ALL SELECT m.id, 2024, 19.5, 'D01', 90 FROM majors m WHERE m.code = 'NG01'
UNION ALL SELECT m.id, 2025, 21.0, 'D01', 100 FROM majors m WHERE m.code = 'NG01'
ON CONFLICT DO NOTHING;

INSERT INTO tuition_scholarships (major_id, tuition_per_year, currency, scholarship_info)
SELECT m.id, 38000000, 'VND', 'Học bổng mérit 10-50% học phí cho học sinh thành tích.' FROM majors m WHERE m.code = 'CNTT01'
UNION ALL SELECT m.id, 42000000, 'VND', 'Học bổng xuất sắc 20% học phí.' FROM majors m WHERE m.code = 'CNTT02'
UNION ALL SELECT m.id, 32000000, 'VND', 'Học bổng khuyến khích 15% học phí.' FROM majors m WHERE m.code = 'KE01'
UNION ALL SELECT m.id, 25000000, 'VND', 'Học bổng xã hội 10% học phí.' FROM majors m WHERE m.code = 'NG01'
UNION ALL SELECT m.id, 30000000, 'VND', 'Học bổng tạo preiem 12% học phí.' FROM majors m WHERE m.code = 'TT01'
ON CONFLICT DO NOTHING;

INSERT INTO admission_methods (method_name, description, requirements) VALUES
('Xét điểm thi THPTQG', 'Xét tuyển dựa trên điểm thi tốt nghiệp phổ thông quốc gia.', 'Điểm thi 3 môn kết hợp với ưu tiên (nếu có).'),
('Xét học bạ', 'Xét tuyển dựa trên kết quả học tập 3 năm THPT.', 'Trung bình các học kỳ từ lớp 10 đến lớp 12.'),
('Xét kết quả học sinh giỏi', 'Ưu tiên tuyển sinh cho học sinh giỏi cấp quốc gia, cấp tỉnh.', 'Chứng chỉ học sinh giỏi tương ứng ngành.'),
('Cơ sở hỗn hợp', 'Kết hợp điểm thi THPTQG và điểm học bạ theo tỷ trọng quy định.', 'Điểm thi THPTQG + điểm học bạ.')
ON CONFLICT DO NOTHING;

-- Seed knowledge_chunks (embedding sẽ được điền bởi /ingest/seed, để NULL ở đây)
INSERT INTO knowledge_chunks (source, title, content, metadata, embedding) VALUES
('majors', 'Tất cả ngành tuyển sinh', 'Danh sách ngành: Công nghệ thông tin, Khoa học máy tính, Hệ thống thông tin, Kỹ thuật phần mềm, An toàn thông tin, Kinh doanh, Kinh tế, Tài chính ngân hàng, Kế toán, Ngôn ngữ Anh, Ngôn ngữ Hàn Quốc, Ngôn ngữ Nhật Bản, Truyền thông đa phương tiện, Quảng cáo.', '{"category":"majors_list","year":2026}', NULL),
('admission_scores', 'Điểm chuẩn 2023-2025', 'Điểm chuẩn các ngành CNTT, Kinh tế, Ngôn ngữ trong 3 năm 2023, 2024, 2025.', '{"category":"admission_scores","years":[2023,2024,2025]}', NULL),
('tuition_scholarships', 'Học phí và học bổng', 'Học phí mỗi năm và chính sách học bổng cho các ngành tuyển sinh.', '{"category":"tuition","currency":"VND"}', NULL),
('admission_methods', 'Phương thức xét tuyển', 'Thi THPTQG, học bạ, học sinh giỏi, Grundlage hỗn hợp.', '{"category":"admission_methods"}', NULL)
ON CONFLICT DO NOTHING;
