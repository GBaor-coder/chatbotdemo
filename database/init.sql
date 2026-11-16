-- PostgreSQL 16 + pgvector schema
-- Vector dimension: 1024 (BAAI/bge-m3 dense output)
-- Multi-tenant: knowledge_chunks và chat_* có tenant_id để cách ly dữ liệu

CREATE EXTENSION IF NOT EXISTS vector;

-- ============================================================
-- Nhóm migration PLATFORM (áp dụng cho mọi domain)
-- ============================================================

CREATE TABLE IF NOT EXISTS tenants (
    id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    name VARCHAR(255) NOT NULL,
    code VARCHAR(50) NOT NULL UNIQUE,
    status VARCHAR(20) NOT NULL DEFAULT 'active',
    config JSONB DEFAULT '{}'::jsonb,
    created_at TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    updated_at TIMESTAMPTZ NOT NULL DEFAULT NOW()
);

CREATE TABLE IF NOT EXISTS documents (
    id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    tenant_id UUID NOT NULL REFERENCES tenants(id) ON DELETE CASCADE,
    source VARCHAR(255) NOT NULL,
    title VARCHAR(255) NOT NULL,
    doc_type VARCHAR(50) NOT NULL DEFAULT 'text',
    status VARCHAR(20) NOT NULL DEFAULT 'published',
    content_hash VARCHAR(64) NOT NULL,
    metadata JSONB DEFAULT '{}'::jsonb,
    created_at TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    updated_at TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    UNIQUE (tenant_id, source, title)
);

CREATE TABLE IF NOT EXISTS knowledge_chunks (
    id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    tenant_id UUID NOT NULL REFERENCES tenants(id) ON DELETE CASCADE,
    document_id UUID NOT NULL REFERENCES documents(id) ON DELETE CASCADE,
    chunk_index INTEGER NOT NULL DEFAULT 0,
    content TEXT NOT NULL,
    embedding vector(1024),
    content_hash VARCHAR(64) NOT NULL,
    embedding_model VARCHAR(100) NOT NULL DEFAULT 'BAAI/bge-m3',
    embedding_version VARCHAR(100) NOT NULL DEFAULT '1',
    lang VARCHAR(10) NOT NULL DEFAULT 'vi',
    metadata JSONB DEFAULT '{}'::jsonb,
    created_at TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    UNIQUE (tenant_id, document_id, chunk_index)
);

CREATE TABLE IF NOT EXISTS ingestion_jobs (
    id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    tenant_id UUID NOT NULL REFERENCES tenants(id) ON DELETE CASCADE,
    document_id UUID REFERENCES documents(id) ON DELETE SET NULL,
    status VARCHAR(20) NOT NULL DEFAULT 'pending',
    started_at TIMESTAMPTZ,
    finished_at TIMESTAMPTZ,
    error_message TEXT,
    created_at TIMESTAMPTZ NOT NULL DEFAULT NOW()
);

CREATE TABLE IF NOT EXISTS chat_sessions (
    id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    tenant_id UUID NOT NULL REFERENCES tenants(id) ON DELETE CASCADE,
    user_id VARCHAR(100) NOT NULL,
    title VARCHAR(255) NOT NULL DEFAULT 'Cuộc trò chuyện mới',
    created_at TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    updated_at TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    deleted_at TIMESTAMPTZ
);

CREATE TABLE IF NOT EXISTS chat_messages (
    id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    session_id UUID NOT NULL REFERENCES chat_sessions(id) ON DELETE CASCADE,
    role VARCHAR(20) NOT NULL CHECK (role IN ('user','assistant','system')),
    content TEXT NOT NULL,
    citations JSONB DEFAULT '[]'::jsonb,
    metadata JSONB DEFAULT '{}'::jsonb,
    created_at TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    deleted_at TIMESTAMPTZ
);

-- ============================================================
-- Nhóm migration DOMAIN TUYỂN SINH (file migration riêng, domain mẫu đầu tiên)
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

CREATE TABLE IF NOT EXISTS tuition_scholarships (
    id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    major_id UUID NOT NULL REFERENCES majors(id) ON DELETE CASCADE,
    tuition_per_year NUMERIC(12,2) NOT NULL,
    scholarship_info TEXT,
    currency VARCHAR(10) NOT NULL DEFAULT 'VND',
    created_at TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    UNIQUE (major_id)
);

CREATE TABLE IF NOT EXISTS admission_methods (
    id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    method_name VARCHAR(100) NOT NULL,
    description TEXT,
    requirements TEXT,
    created_at TIMESTAMPTZ NOT NULL DEFAULT NOW()
);

-- ============================================================
-- Chỉ số
-- ============================================================

CREATE INDEX IF NOT EXISTS idx_knowledge_embedding ON knowledge_chunks USING hnsw (embedding vector_cosine_ops);
CREATE INDEX IF NOT EXISTS idx_knowledge_tenant_doc ON knowledge_chunks (tenant_id, document_id);
CREATE INDEX IF NOT EXISTS idx_knowledge_metadata ON knowledge_chunks USING GIN (metadata);
CREATE INDEX IF NOT EXISTS idx_documents_tenant ON documents (tenant_id);
CREATE INDEX IF NOT EXISTS idx_ingestion_jobs_tenant ON ingestion_jobs (tenant_id);
CREATE INDEX IF NOT EXISTS idx_chat_sessions_tenant_user ON chat_sessions (tenant_id, user_id, created_at);
CREATE INDEX IF NOT EXISTS idx_chat_messages_session ON chat_messages (session_id, created_at);
CREATE INDEX IF NOT EXISTS idx_admission_scores_major_year ON admission_scores (major_id, year);
CREATE INDEX IF NOT EXISTS idx_tuition_scholarships_major ON tuition_scholarships (major_id);

-- ============================================================
-- Dữ liệu mẫu: tenants (ít nhất 2 tenant để test cách ly)
-- ============================================================

INSERT INTO tenants (id, name, code, status, config) VALUES
('11111111-1111-1111-1111-111111111111', 'Trường Đại học Mẫu A', 'UNIV-A', 'active', '{"top_k": 5, "min_score": 0.3, "refusal_message": "Xin lỗi, hiện tại tôi chỉ hỗ trợ giải đáp các thông tin liên quan đến tuyển sinh của Nhà trường dựa trên dữ liệu chính thức. Bạn vui lòng đặt câu hỏi về ngành học, điểm chuẩn, học phí hoặc phương thức xét tuyển nhé!", "system_prompt_template": "Bạn là trợ lý tư vấn tuyển sinh."}') 
ON CONFLICT (code) DO NOTHING;

INSERT INTO tenants (id, name, code, status, config) VALUES
('22222222-2222-2222-2222-222222222222', 'Trường Đại học Mẫu B', 'UNIV-B', 'active', '{"top_k": 5, "min_score": 0.3, "refusal_message": "Xin lỗi, hiện tại tôi chỉ hỗ trợ giải đáp các thông tin liên quan đến tuyển sinh của Nhà trường dựa trên dữ liệu chính thức. Bạn vui lòng đặt câu hỏi về ngành học, điểm chuẩn, học phí hoặc phương thức xét tuyển nhé!", "system_prompt_template": "Bạn là trợ lý tư vấn tuyển sinh."}') 
ON CONFLICT (code) DO NOTHING;

-- ============================================================
-- Dữ liệu mẫu ngành tuyển sinh (gắn với tenant UNIV-A qua documents sau ingestion)
-- ============================================================

INSERT INTO majors (code, name, block, description, subject_combination, quota) VALUES
('CNTT01', 'Công nghệ thông tin', 'CNTT', 'Ngành đào tạo về phát triển phần mềm, hệ thống thông tin và công nghệ số.', 'A00', 300),
('CNTT02', 'Khoa học máy tính', 'CNTT', 'Ngành tập trung vào lý thuyết tính toán, thuật toán và trí tuệ nhân tạo.', 'A00', 180),
('CNTT03', 'Hệ thống thông tin', 'CNTT', 'Ngành kết hợp công nghệ thông tin và quản trị doanh nghiệp.', 'A00', 150),
('CNTT04', 'Kỹ thuật phần mềm', 'CNTT', 'Ngành đào tạo về quy trình phát triển phần mềm chuẩnizat.', 'A00', 160),
('CNTT05', 'An toàn thông tin', 'CNTT', 'Ngành về bảo mật mạng, mã hóa và an toàn thông tin.', 'A00', 100),
('KE01', 'Kinh doanh', 'Kinh tế', 'Ngành về quản trị doanh nghiệp, marketing và kinh doanh quốc tế.', 'D01', 250),
('KE02', 'Kinh tế', 'Kinh tế', 'Ngành nghiên cứu về kinh tế vĩ mô và kinh tế vi mô.', 'D01', 120),
('KE03', 'Tài chính ngân hàng', 'Kinh tế', 'Ngành về hoạt động tài chính, ngân hàng và đầu tư.', 'D01', 140),
('KE04', 'Kế toán', 'Kinh tế', 'Ngành về kế toán doanh nghiệp, kiểm toán và kiểm soát nội bộ.', 'D01', 130),
('NG01', 'Ngôn ngữ Anh', 'Ngôn ngữ', 'Ngành đào tạo giáo viên và chuyên viên ngôn ngữ Anh.', 'D01', 100),
('NG02', 'Ngôn ngữ Hàn Quốc', 'Ngôn ngữ', 'Ngành về ngôn ngữ và văn hóa Hàn Quốc.', 'D01', 60),
('NG03', 'Ngôn ngữ Nhật Bản', 'Ngôn ngữ', 'Ngành về ngôn ngữ và văn hóa Nhật Bản.', 'D01', 60),
('TT01', 'Truyền thông đa phương tiện', 'Truyền thông', 'Ngành về nội dung truyền thông số và đa phương tiện.', 'D01', 80),
('TT02', 'Quảng cáo', 'Truyền thông', 'Ngành về sáng tạo quảng cáo và truyền thông điệp.', 'D01', 70)
ON CONFLICT (code) DO NOTHING;

INSERT INTO admission_scores (major_id, year, min_score, subject_combination, quota)
SELECT m.id, 2023, 24.50, 'A00', 280 FROM majors m WHERE m.code = 'CNTT01'
UNION ALL SELECT m.id, 2024, 26.00, 'A00', 290 FROM majors m WHERE m.code = 'CNTT01'
UNION ALL SELECT m.id, 2025, 27.50, 'A00', 300 FROM majors m WHERE m.code = 'CNTT01'
UNION ALL SELECT m.id, 2023, 23.00, 'A00', 170 FROM majors m WHERE m.code = 'CNTT02'
UNION ALL SELECT m.id, 2024, 24.50, 'A00', 175 FROM majors m WHERE m.code = 'CNTT02'
UNION ALL SELECT m.id, 2025, 26.00, 'A00', 180 FROM majors m WHERE m.code = 'CNTT02'
UNION ALL SELECT m.id, 2023, 21.00, 'A00', 140 FROM majors m WHERE m.code = 'CNTT03'
UNION ALL SELECT m.id, 2024, 22.50, 'A00', 145 FROM majors m WHERE m.code = 'CNTT03'
UNION ALL SELECT m.id, 2025, 24.00, 'A00', 150 FROM majors m WHERE m.code = 'CNTT03'
UNION ALL SELECT m.id, 2023, 19.50, 'D01', 230 FROM majors m WHERE m.code = 'KE01'
UNION ALL SELECT m.id, 2024, 21.00, 'D01', 240 FROM majors m WHERE m.code = 'KE01'
UNION ALL SELECT m.id, 2025, 22.50, 'D01', 250 FROM majors m WHERE m.code = 'KE01'
UNION ALL SELECT m.id, 2023, 18.00, 'D01', 110 FROM majors m WHERE m.code = 'KE02'
UNION ALL SELECT m.id, 2024, 19.50, 'D01', 115 FROM majors m WHERE m.code = 'KE02'
UNION ALL SELECT m.id, 2025, 21.00, 'D01', 120 FROM majors m WHERE m.code = 'KE02'
UNION ALL SELECT m.id, 2023, 19.00, 'D01', 130 FROM majors m WHERE m.code = 'KE03'
UNION ALL SELECT m.id, 2024, 20.50, 'D01', 135 FROM majors m WHERE m.code = 'KE03'
UNION ALL SELECT m.id, 2025, 22.00, 'D01', 140 FROM majors m WHERE m.code = 'KE03'
UNION ALL SELECT m.id, 2023, 17.50, 'D01', 120 FROM majors m WHERE m.code = 'KE04'
UNION ALL SELECT m.id, 2024, 19.00, 'D01', 125 FROM majors m WHERE m.code = 'KE04'
UNION ALL SELECT m.id, 2025, 20.50, 'D01', 130 FROM majors m WHERE m.code = 'KE04'
UNION ALL SELECT m.id, 2023, 17.00, 'D01', 90 FROM majors m WHERE m.code = 'NG01'
UNION ALL SELECT m.id, 2024, 18.50, 'D01', 95 FROM majors m WHERE m.code = 'NG01'
UNION ALL SELECT m.id, 2025, 20.00, 'D01', 100 FROM majors m WHERE m.code = 'NG01'
UNION ALL SELECT m.id, 2023, 16.00, 'D01', 55 FROM majors m WHERE m.code = 'NG02'
UNION ALL SELECT m.id, 2024, 17.50, 'D01', 58 FROM majors m WHERE m.code = 'NG02'
UNION ALL SELECT m.id, 2025, 19.00, 'D01', 60 FROM majors m WHERE m.code = 'NG02'
UNION ALL SELECT m.id, 2023, 16.00, 'D01', 55 FROM majors m WHERE m.code = 'NG03'
UNION ALL SELECT m.id, 2024, 17.50, 'D01', 58 FROM majors m WHERE m.code = 'NG03'
UNION ALL SELECT m.id, 2025, 19.00, 'D01', 60 FROM majors m WHERE m.code = 'NG03'
UNION ALL SELECT m.id, 2023, 17.50, 'D01', 75 FROM majors m WHERE m.code = 'TT01'
UNION ALL SELECT m.id, 2024, 19.00, 'D01', 78 FROM majors m WHERE m.code = 'TT01'
UNION ALL SELECT m.id, 2025, 20.50, 'D01', 80 FROM majors m WHERE m.code = 'TT01'
UNION ALL SELECT m.id, 2023, 17.00, 'D01', 65 FROM majors m WHERE m.code = 'TT02'
UNION ALL SELECT m.id, 2024, 18.50, 'D01', 68 FROM majors m WHERE m.code = 'TT02'
UNION ALL SELECT m.id, 2025, 20.00, 'D01', 70 FROM majors m WHERE m.code = 'TT02'
ON CONFLICT DO NOTHING;

INSERT INTO tuition_scholarships (major_id, tuition_per_year, currency, scholarship_info)
SELECT m.id, 38000000, 'VND', 'Học bổng mérit 10-50% học phí cho học sinh thành tích.' FROM majors m WHERE m.code = 'CNTT01'
UNION ALL SELECT m.id, 42000000, 'VND', 'Học bổng xuất sắc 20% học phí.' FROM majors m WHERE m.code = 'CNTT02'
UNION ALL SELECT m.id, 36000000, 'VND', 'Học bổng khuyến khích 15% học phí cho điểm thi cao.' FROM majors m WHERE m.code = 'CNTT03'
UNION ALL SELECT m.id, 37000000, 'VND', 'Học bổng 10% học phí; hỗ trợ du học hợp tác quốc tế.' FROM majors m WHERE m.code = 'CNTT04'
UNION ALL SELECT m.id, 40000000, 'VND', 'Học bổng an toàn thông tin 25% học phí cho sinh viên giỏi.' FROM majors m WHERE m.code = 'CNTT05'
UNION ALL SELECT m.id, 32000000, 'VND', 'Học bổng khuyến khích 15% học phí; hỗ trợ thực tế doanh nghiệp.' FROM majors m WHERE m.code = 'KE01'
UNION ALL SELECT m.id, 28000000, 'VND', 'Học bổng nghiên cứu kinh tế 10% học phí.' FROM majors m WHERE m.code = 'KE02'
UNION ALL SELECT m.id, 30000000, 'VND', 'Học bổng tài chính ngân hàng 20% học phí; hỗ trợ làm việc tại ngân hàng.' FROM majors m WHERE m.code = 'KE03'
UNION ALL SELECT m.id, 29000000, 'VND', 'Học bổng kế toán 10% học phí; hỗ trợ chứng chỉ kế toán.' FROM majors m WHERE m.code = 'KE04'
UNION ALL SELECT m.id, 25000000, 'VND', 'Học bổng ngoại ngữ 15% học phí; hỗ trợ du học Anh, Mỹ.' FROM majors m WHERE m.code = 'NG01'
UNION ALL SELECT m.id, 24000000, 'VND', 'Học bổng Hàn Quốc 20% học phí; hỗ trợ trao đổi sinh viên tại Hàn Quốc.' FROM majors m WHERE m.code = 'NG02'
UNION ALL SELECT m.id, 24000000, 'VND', 'Học bổng Nhật Bản 20% học phí; hỗ trợ trao đổi sinh viên tại Nhật Bản.' FROM majors m WHERE m.code = 'NG03'
UNION ALL SELECT m.id, 30000000, 'VND', 'Học bổng truyền thông 12% học phí; hỗ trợ thực tập tại các kênh truyền hình.' FROM majors m WHERE m.code = 'TT01'
UNION ALL SELECT m.id, 28000000, 'VND', 'Học bổng quảng cáo 10% học phí; hỗ trợ thực tập tại các công ty quảng cáo.' FROM majors m WHERE m.code = 'TT02'
ON CONFLICT DO NOTHING;

INSERT INTO admission_methods (method_name, description, requirements) VALUES
('Xét điểm thi THPTQG', 'Xét tuyển dựa trên điểm thi tốt nghiệp phổ thông quốc gia.', 'Điểm thi 3 môn kết hợp với điểm ưu tiên (nếu có).'),
('Xét học bạ', 'Xét tuyển dựa trên kết quả học tập 3 năm THPT.', 'Trung bình các học kỳ từ lớp 10 đến lớp 12.'),
('Xét kết quả học sinh giỏi', 'Ưu tiên tuyển sinh cho học sinh giỏi cấp quốc gia, cấp tỉnh.', 'Chứng chỉ học sinh giỏi tương ứng ngành.'),
('Cơ sở hỗn hợp', 'Kết hợp điểm thi THPTQG và điểm học bạ theo tỷ trọng quy định.', 'Điểm thi THPTQG + điểm học bạ theo tỷ trọng đã công bố.')
ON CONFLICT DO NOTHING;
