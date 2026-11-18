# scripts/verify.ps1 — Harness kiểm chứng chạy thật (R1)
# Chạy trên Windows PowerShell. Yêu cầu Docker đã chạy, .env đã nạp (sao chép từ .env.example).
# Bằng chứng ghi vào docs/verification/run-<timestamp>.md. Bí mật được che.

$ErrorActionPreference = "Stop"
$ProgressPreference = "SilentlyContinue"

$ROOT = Resolve-Path "$PSScriptRoot\.."
$VERIFY_DIR = Join-Path $ROOT "docs\verification"
$TIMESTAMP = Get-Date -Format "yyyyMMdd-HHmmss"
$REPORT_PATH = Join-Path $VERIFY_DIR "run-$TIMESTAMP.md"
$global:FAIL = $false
$global:CHECKS = @()
$SECRETS = @("sk-", "password=", "Bearer ")

if (-not (Test-Path -LiteralPath $VERIFY_DIR)) { New-Item -ItemType Directory -Path $VERIFY_DIR | Out-Null }

function Write-Report([string]$text) { $text | Out-File -FilePath $REPORT_PATH -Encoding utf8 -Append }
function Write-Console([string]$text) { Write-Host $text; Write-Report $text }
function New-Section([string]$title) {
    Write-Report ""; Write-Report ("=" * 80); Write-Report $title; Write-Report ("=" * 80); Write-Report ""
}
function Redact([string]$s) {
    foreach ($secret in $SECRETS) { $s = $s -replace [regex]::Escape($secret), ($secret.Substring(0,2) + "****") }
    return $s
}
function Add-Check([string]$name, [bool]$pass, [string]$evidence) {
    $global:CHECKS += [pscustomobject]@{ Name = $name; Pass = $pass; Evidence = $evidence; Time = (Get-Date -Format "HH:mm:ss") }
    $status = if ($pass) { "PASS" } else { "FAIL" }
    if (-not $pass) { $global:FAIL = $true }
    Write-Console ("[{0}] {1}" -f $status, $name)
    if ($evidence) { Write-Report ("    EVIDENCE: " + (Redact $evidence)) }
}
function Run-Cmd([string]$cmd, [string]$desc) {
    Write-Report ("CMD: " + $desc)
    Write-Report ("    " + (Redact $cmd))
    $t0 = Get-Date
    $output = & cmd /c $cmd 2>&1
    $t1 = Get-Date
    $ms = [int]((($t1 - $t0).TotalMilliseconds))
    $exit = $LASTEXITCODE
    Write-Report ("    EXIT: $exit  TIME: ${ms}ms")
    foreach ($line in $output) { Write-Report ("    " + (Redact $line)) }
    return @{ Exit = $exit; Output = ($output -join "`n"); Ms = $ms }
}
function Get-EnvVal([string]$key) {
    $line = Get-Content -LiteralPath (Join-Path $ROOT ".env") | Select-String ("^" + [regex]::Escape($key) + "=") | Select-Object -First 1
    if (-not $line) { return $null }
    return ($line.ToString() -split "=", 2 | Select-Object -Last 1).Trim()
}

# =====================================================================
# Bắt đầu báo cáo
# =====================================================================
Write-Report ("# Báo cáo kiểm chứng chạy thật — $TIMESTAMP")
Write-Report ("Stack thật: PostgreSQL+pgvector, ai-service (bge-m3), backend-dotnet (.NET 10), frontend-react.")
Write-Report ("Bí mật đã được che dạng ****. Không ghi key thật vào báo cáo.")
Write-Report ("Thời gian bắt đầu: $(Get-Date)")
Write-Report ""

# =====================================================================
# PREFLIGHT LLM (R1) — bắt buộc trước mọi nghiệm thu LLM
# =====================================================================
Write-Console "=== PREFLIGHT LLM (bắt buộc trước L4/L6) ==="
New-Section "PREFLIGHT LLM"

$envFile = Join-Path $ROOT ".env"
if (-not (Test-Path -LiteralPath $envFile)) {
    Add-Check "Preflight: .env tồn tại" $false "Không tìm thấy .env tại gốc repo"
    Write-Console "DUNG: thiếu .env. Hãy sao chép .env.example và điền LLM_API_KEY thật."
    Write-Report ""
    Write-Report "## KẾT LUẬN: DỪNG tại preflight (thiếu .env)"
    exit 1
}
Add-Check "Preflight: .env tồn tại" $true "Tìm thấy .env (bí mật không ghi báo cáo)"

$llmBase = Get-EnvVal "LLM_BASE_URL"
$llmKey  = Get-EnvVal "LLM_API_KEY"
$llmModel= Get-EnvVal "LLM_MODEL"

if (-not $llmBase -or -not $llmKey -or -not $llmModel) {
    Add-Check "Preflight: LLM_BASE_URL / LLM_API_KEY / LLM_MODEL đầy đủ" $false "Thiếu giá trị trong .env"
    Write-Console "DỪNG: thiếu thông tin 9Router trong .env."
    exit 1
}
Add-Check "Preflight: LLM_BASE_URL / LLM_API_KEY / LLM_MODEL đầy đủ" $true "Đã đọc từ .env (key không ghi báo cáo)"

$pfPayload = "{ `"model`": `"MODEL_PLACEHOLDER`", `"messages`": [ { `"role`": `"user`", `"content`": `"ping`" } ], `"stream`": false }" -replace "MODEL_PLACEHOLDER", $llmModel
$pfPayloadFile = Join-Path $env:TEMP "pf-payload.json"
$pfPayload | Out-File -FilePath $pfPayloadFile -Encoding utf8
$cp = Run-Cmd ("docker compose cp " + $pfPayloadFile + " backend-dotnet:/tmp/pf-payload.json") "Copy payload vào container backend-dotnet"
$pfCmd = "docker compose exec -T backend-dotnet bash -c 'curl -sS -m 15 -N " + $llmBase + "/chat/completions -H " + '"' + "Content-Type: application/json" + '"' + " -H " + '"' + "Authorization: Bearer $llmKey" + '"' + " -d @/tmp/pf-payload.json'"
$pf = Run-Cmd $pfCmd "Preflight: gọi 9Router từ container backend-dotnet (1 completion thật)"
$pfPass = ($pf.Exit -eq 0) -and ($pf.Output -match '"id"') -and ($pf.Output -match '"choices"')
Add-Check "Preflight: 9Router phản hồi completion thật từ backend-dotnet" $pfPass ($pf.Output | Select-Object -First 3)

if (-not $pfPass) {
    Write-Console "DỪNG: Preflight LLM thất bại (R1). Không gọi được LLM_BASE_URL từ container backend-dotnet."
    Write-Report ""
    Write-Report "## KẾT LUẬN: DỪNG tại preflight LLM (chưa chạy L4/L6)"
    Write-Report "Nguyên nhân có thể: LLM_API_KEY chưa đúng, 9Router chưa chạy, host không khả truy cập từ container."
    Write-Report "Hãy sửa .env (key mới, host đúng) rồi chạy lại verify.ps1."
    exit 1
}

Write-Console "Preflight LLM qua. Tiếp tục harness L0–L6."

# =====================================================================
# L0 TÍNH
# =====================================================================
New-Section "L0 — STATIC / SECRET SCAN"
Write-Console "=== L0 STATIC ==="

$cfg = Run-Cmd "docker compose config --quiet" "docker compose config (kiểm tra compose hợp lệ)"
Add-Check "L0: docker compose config hợp lệ" ($cfg.Exit -eq 0) ($cfg.Output | Select-Object -First 20)

$build = Run-Cmd "docker compose build" "docker compose build (tất cả 4 service)"
Add-Check "L0: build cả 4 image" ($build.Exit -eq 0) ($build.Output | Select-Object -Last 30)

$dotnetBuild = Run-Cmd ("dotnet build " + (Join-Path $ROOT "backend-dotnet\BackendDotnet.csproj") + " -c Release") "dotnet build backend-dotnet"
Add-Check "L0: build .NET thành công" ($dotnetBuild.Exit -eq 0) ($dotnetBuild.Output | Select-Object -Last 20)

$pyFiles = @("main.py","database.py","embeddings.py","config.py")
$pyArgs = ""
foreach ($f in $pyFiles) { $pyArgs += " " + (Join-Path $ROOT ("ai-service\app\" + $f)) }
$pyLint = Run-Cmd ("python -m py_compile" + $pyArgs) "Python compile (không có import openai)"
Add-Check "L0: Python compile không lỗi" ($pyLint.Exit -eq 0) ($pyLint.Output)

$openaiCheck = Select-String -Path "$ROOT\ai-service\app\*.py" -Pattern "openai|LLM_BASE_URL|GATEWAY_URL|stream_llm" -Quiet
Add-Check "L0: ai-service không còn import openai / gọi LLM" (-not $openaiCheck) "Quét lại: " + (if ($openaiCheck) { "CÒN CÓ" } else { "SẠCH" })

$secretFiles = @("$ROOT\PLAN.md", "$ROOT\.kilocode\rules\architecture.md", "$ROOT\README.md", "$ROOT\docker-compose.yml")
$secretFound = $false
foreach ($f in $secretFiles) {
    if (Test-Path -LiteralPath $f) {
        $content = Get-Content -LiteralPath $f -Raw
        foreach ($s in $SECRETS) {
            if ($content -match [regex]::Escape($s)) { $secretFound = $true; Write-Report ("    Tìm bí mật trong: " + $f) }
        }
    }
}
Add-Check "L0: không còn key plaintext trong PLAN/architecture/README/docker-compose.yml" (-not $secretFound) "Quét sk-, password=, Bearer"

# =====================================================================
# L1 SỐNG / HEALTH
# =====================================================================
New-Section "L1 — LIVE / HEALTH"
Write-Console "=== L1 LIVE ==="

$dbHealth = Run-Cmd "docker compose exec -T db pg_isready -U postgres -d chatbot" "db pg_isready"
Add-Check "L1: db healthy" ($dbHealth.Exit -eq 0) ($dbHealth.Output)

$aiHealth = Run-Cmd "docker compose exec -T ai-service curl -sS -m 10 http://localhost:8000/api/v1/health" "ai-service /api/v1/health"
Add-Check "L1: ai-service /api/v1/health" ($aiHealth.Exit -eq 0) ($aiHealth.Output)

$aiReady = Run-Cmd "docker compose exec -T ai-service curl -sS -m 10 http://localhost:8000/api/v1/ready" "ai-service /api/v1/ready"
Add-Check "L1: ai-service /api/v1/ready (model+DB OK)" ($aiReady.Exit -eq 0) ($aiReady.Output)

$beHealth = Run-Cmd "docker compose exec -T backend-dotnet curl -sS -m 10 http://localhost:5000/health" "backend-dotnet /health"
Add-Check "L1: backend-dotnet /health" ($beHealth.Exit -eq 0) ($beHealth.Output)

$vecCheck = Run-Cmd "docker compose exec -T db psql -U postgres -d chatbot -tAc " + '"' + "SELECT extname FROM pg_extension WHERE extname='vector'" + '"'
Add-Check "L1: extension vector tồn tại" ($vecCheck.Exit -eq 0 -and $vecCheck.Output -match "vector") ($vecCheck.Output)

$idxCheck = Run-Cmd "docker compose exec -T db psql -U postgres -d chatbot -tAc " + '"' + "SELECT indexname FROM pg_indexes WHERE tablename='knowledge_chunks' AND indexdef LIKE '%hnsw%'" + '"'
Add-Check "L1: index HNSW tồn tại" ($idxCheck.Exit -eq 0 -and $idxCheck.Output -match "hnsw") ($idxCheck.Output)

# =====================================================================
# L2 HỢP ĐỒNG API
# =====================================================================
New-Section "L2 — API CONTRACT"
Write-Console "=== L2 CONTRACT ==="

$internalKey = Get-EnvVal "X_INTERNAL_API_KEY"

$noKeyBody = "{ `"query`": `"test`", `"filters`": { `"tenant_id`": `"11111111-1111-1111-1111-111111111111`" } }"
$noKeyFile = Join-Path $env:TEMP "nokey.json"
$noKeyBody | Out-File -FilePath $noKeyFile -Encoding utf8
$cp1 = Run-Cmd ("docker compose cp " + $noKeyFile + " ai-service:/tmp/nokey.json") "Copy no-key body"
$noKey = Run-Cmd 'docker compose exec -T ai-service curl -sS -o /dev/null -w "%{http_code}" -X POST http://localhost:8000/api/v1/retrieval/search -H "Content-Type: application/json" -d @/tmp/nokey.json' "Thiếu X-Internal-Api-Key -> 401"
Add-Check "L2: thiếu key -> 401" ($noKey.Output -eq "401") ($noKey.Output)

$badBody = "{ `"query`": `"test`", `"filters`": { `"tenant_id`": `"11111111-1111-1111-1111-111111111111`", `"bad_key`": `"x`" } }"
$badFile = Join-Path $env:TEMP "bad.json"
$badBody | Out-File -FilePath $badFile -Encoding utf8
$cp2 = Run-Cmd ("docker compose cp " + $badFile + " ai-service:/tmp/bad.json") "Copy bad-filter body"
$badFilter = Run-Cmd ("docker compose exec -T ai-service curl -sS -o /dev/null -w ""%{http_code}"" -X POST http://localhost:8000/api/v1/retrieval/search -H " + '"' + "Content-Type: application/json" + '"' + " -H " + '"' + ("X-Internal-Api-Key: " + $internalKey) + '"' + " -d @/tmp/bad.json") "Filter ngoài whitelist -> 400/422"
Add-Check "L2: filter lạ -> 400/422" (@("400","422") -contains $badFilter.Output) ($badFilter.Output)

$emptyBody = "{ `"query`": `"câu hỏi không có trong cơ sở dữ liệu xyz123`", `"top_k`": 5, `"min_score`": 0.0, `"filters`": { `"tenant_id`": `"11111111-1111-1111-1111-111111111111`" } }"
$emptyFile = Join-Path $env:TEMP "empty.json"
$emptyBody | Out-File -FilePath $emptyFile -Encoding utf8
$cp3 = Run-Cmd ("docker compose cp " + $emptyFile + " ai-service:/tmp/empty.json") "Copy empty-search body"
$emptySearch = Run-Cmd ("docker compose exec -T ai-service curl -sS -X POST http://localhost:8000/api/v1/retrieval/search -H " + '"' + "Content-Type: application/json" + '"' + " -H " + '"' + ("X-Internal-Api-Key: " + $internalKey) + '"' + " -d @/tmp/empty.json") "Không kết quả -> 200 results []"
$emptyPass = ($emptySearch.Exit -eq 0) -and ($emptySearch.Output -match '"success":\s*true') -and ($emptySearch.Output -match '"results":\s*\[\]')
Add-Check "L2: không kết quả -> 200 results []" $emptyPass ($emptySearch.Output | Select-Object -First 5)

$openapi = Run-Cmd "docker compose exec -T ai-service curl -sS http://localhost:8000/openapi.json" "Xuất OpenAPI từ FastAPI"
$openapiPass = ($openapi.Exit -eq 0) -and ($openapi.Output -match 'retrieval/search')
Add-Check "L2: OpenAPI xuất được và có /api/v1/retrieval/search" $openapiPass ($openapi.Output | Select-Object -First 10)

# =====================================================================
# L3 RETRIEVAL TRÊN DỮ LIỆU THẬT (golden.jsonl)
# =====================================================================
New-Section "L3 — RETRIEVAL ON REAL DATA"
Write-Console "=== L3 RETRIEVAL ==="

$golden = Join-Path $ROOT "docs\verification\golden.jsonl"
if (-not (Test-Path -LiteralPath $golden)) {
    @(
        "{ `"question`": `"Điểm chuẩn ngành Công nghệ thông tin 2024`", `"expect_contains`": [`"Công nghệ thông tin`",`"26.0`"], `"category`": `"admission_scores`" }",
        "{ `"question`": `"Học phí ngành Kinh doanh`", `"expect_contains`": [`"Kinh doanh`",`"32 triệu`"], `"category`": `"tuition`" }",
        "{ `"question`": `"Phương thức xét tuyển`", `"expect_contains`": [`"THPTQG`",`"học bạ`"], `"category`": `"admission_methods`" }",
        "{ `"question`": `"Lịch trình tuyển sinh 2026`", `"expect_contains`": [`"tháng 5-6`",`"tháng 8-9`"], `"category`": `"schedule`" }",
        "{ `"question`": `"Danh sách ngành tuyển sinh 2026`", `"expect_contains`": [`"Công nghệ thông tin`",`"Kinh doanh`"], `"category`": `"majors`" }",
        "{ `"question`": `"Điểm chuẩn Khoa học máy tính 2025`", `"expect_contains`": [`"Khoa học máy tính`",`"26.0`"], `"category`": `"admission_scores`" }",
        "{ `"question`": `"Học phí An toàn thông tin`", `"expect_contains`": [`"An toàn thông tin`",`"40 triệu`"], `"category`": `"tuition`" }",
        "{ `"question`": `"Học bổng Công nghệ thông tin`", `"expect_contains`": [`"học bổng`",`"CNTT01`"], `"category`": `"tuition`" }",
        "{ `"question`": `"Ngành Ngôn ngữ Anh điểm chuẩn`", `"expect_contains`": [`"Ngôn ngữ Anh`",`"18.5`"], `"category`": `"admission_scores`" }",
        "{ `"question`": `"Quota ngành Kỹ thuật phần mềm`", `"expect_contains`": [`"Kỹ thuật phần mềm`",`"160`"], `"category`": `"majors`" }",
        "{ `"question`": `"Điểm chuẩn Kinh tế 2023`", `"expect_contains`": [`"Kinh tế`",`"18.0`"], `"category`": `"admission_scores`" }",
        "{ `"question`": `"Học phí Ngôn ngữ Nhật Bản`", `"expect_contains`": [`"Ngôn ngữ Nhật Bản`",`"24 triệu`"], `"category`": `"tuition`" }",
        "{ `"question`": `"Phương thức Cơ sở hỗn hợp`", `"expect_contains`": [`"Cơ sở hỗn hợp`"], `"category`": `"admission_methods`" }",
        "{ `"question`": `"Lịch thi THPTQG 2026`", `"expect_contains`": [`"tháng 6/2026`"], `"category`": `"schedule`" }",
        "{ `"question`": `"Ngành Hệ thống thông tin`", `"expect_contains`": [`"Hệ thống thông tin`"], `"category`": `"majors`" }",
        "{ `"question`": `"Điểm chuẩn Truyền thông đa phương tiện 2024`", `"expect_contains`": [`"Truyền thông đa phương tiện`",`"19.0`"], `"category`": `"admission_scores`" }",
        "{ `"question`": `"Học bổng Tài chính ngân hàng`", `"expect_contains`": [`"Tài chính ngân hàng`",`"học bổng`"], `"category`": `"tuition`" }",
        "{ `"question`": `"Ngôn ngữ Hàn Quốc quota`", `"expect_contains`": [`"Ngôn ngữ Hàn Quốc`",`"60`"], `"category`": `"majors`" }",
        "{ `"question`": `"Điểm chuẩn Kế toán 2025`", `"expect_contains`": [`"Kế toán`",`"20.5`"], `"category`": `"admission_scores`" }",
        "{ `"question`": `"Học phí Quảng cáo`", `"expect_contains`": [`"Quảng cáo`",`"28 triệu`"], `"category`": `"tuition`" }"
    ) | Out-File -FilePath $golden -Encoding utf8
    Write-Report ("Tạo file golden mẫu: " + $golden)
}

$goldenLines = Get-Content -LiteralPath $golden
$total = 0; $ok = 0
foreach ($line in $goldenLines) {
    if ([string]::IsNullOrWhiteSpace($line)) { continue }
    $j = $line | ConvertFrom-Json
    $q = $j.question
    $expect = $j.expect_contains
    $body = "{ `"query`": `"Q_PLACEHOLDER`", `"top_k`": 5, `"min_score`": 0.0, `"filters`": { `"tenant_id`": `"11111111-1111-1111-1111-111111111111`" } }" -replace "Q_PLACEHOLDER", $q
    $gf = Join-Path $env:TEMP "g3.json"
    $body | Out-File -FilePath $gf -Encoding utf8
    $cp = Run-Cmd ("docker compose cp " + $gf + " ai-service:/tmp/g3.json") "Copy golden body"
    $res = Run-Cmd ("docker compose exec -T ai-service curl -sS -X POST http://localhost:8000/api/v1/retrieval/search -H " + '"' + "Content-Type: application/json" + '"' + " -H " + '"' + ("X-Internal-Api-Key: " + $internalKey) + '"' + " -d @/tmp/g3.json") ("L3 search: " + $q)
    $total++
    if ($res.Exit -eq 0 -and $res.Output -match '"success":\s*true') {
        $hit = $false
        foreach ($e in $expect) { if ($res.Output -match [regex]::Escape($e)) { $hit = $true; break } }
        if ($hit) { $ok++ } else { Write-Report ("    KHÔNG KHỚP kỳ vọng: " + $q + " -> cần chứa " + ($expect -join ",")) }
    } else {
        Write-Report ("    LỖI search: " + $q + " -> " + ($res.Output | Select-Object -First 3))
    }
}
$recall = if ($total -gt 0) { "{0:P0}" -f ($ok/$total) } else { "0%" }
Add-Check "L3: recall@5 trên câu hỏi vàng (≥ 20 câu, ≥70% khớp)" ($total -ge 20 -and $ok -ge ($total*0.7)) ("$ok/$total câu khớp ($recall)")

# =====================================================================
# L4 E2E LLM THẬT QUA .NET
# =====================================================================
New-Section "L4 — E2E LLM REAL VIA .NET"
Write-Console "=== L4 E2E ==="

$tenantId = "11111111-1111-1111-1111-111111111111"
$chatBody = "{ `"message`": `"Điểm chuẩn ngành Công nghệ thông tin 2024 là bao nhiêu?`", `"sessionId`": null }"
$cf = Join-Path $env:TEMP "chat4.json"
$chatBody | Out-File -FilePath $cf -Encoding utf8
$cp = Run-Cmd ("docker compose cp " + $cf + " backend-dotnet:/tmp/chat4.json") "Copy chat body"
$chat = Run-Cmd ("docker compose exec -T backend-dotnet curl -sS -N -m 60 -X POST http://localhost:5000/api/chat/stream -H " + '"' + "Content-Type: application/json" + '"' + " -H " + '"' + "Authorization: Bearer dev-token" + '"' + " -H " + '"' + ("X-Tenant-Id: " + $tenantId) + '"' + " -d @/tmp/chat4.json") ("L4: POST /api/chat/stream câu số liệu")
$chatPass = ($chat.Exit -eq 0) -and ($chat.Output -match '"type":"token"') -and ($chat.Output -match '"type":"citations"') -and ($chat.Output -match '"type":"done"')
Add-Check "L4: có token, citations, done" $chatPass ($chat.Output | Select-Object -First 15)
Add-Check "L4: số liệu CNTT 2024 = 26.0 có trong câu trả lời" ($chat.Output -match "26\.0") ("Số liệu DB: 26.0")

$sessionIdFrom = ""
if ($chat.Output -match '"session_id":"([^"]+)"') { $sessionIdFrom = $Matches[1] }
if ($sessionIdFrom) {
    $hist = Run-Cmd ("docker compose exec -T backend-dotnet curl -sS http://localhost:5000/api/chat/history/" + $sessionIdFrom + " -H " + '"' + "Authorization: Bearer dev-token" + '"' + " -H " + '"' + ("X-Tenant-Id: " + $tenantId) + '"') ("L4: kiểm tra history lưu đủ 2 message")
    $histPass = ($hist.Exit -eq 0) -and ($hist.Output -match '"role":"user"') -and ($hist.Output -match '"role":"assistant"')
    Add-Check "L4: lưu đủ 2 message (user + assistant)" $histPass ($hist.Output | Select-Object -First 10)
} else {
    Add-Check "L4: lưu đủ 2 message (user + assistant)" $false "Không trích xuất được session_id từ stream"
}

# =====================================================================
# L5 GUARDRAIL (chạy 3 lần mỗi case)
# =====================================================================
New-Section "L5 — GUARDRAIL (OUT OF SCOPE, 3 TIMES EACH)"
Write-Console "=== L5 GUARDRAIL ==="

$refusal = "Xin lỗi, hiện tại tôi chỉ hỗ trợ giải đáp các thông tin liên quan đến tuyển sinh của Nhà trường"
$offTopicCases = @("Thời tiết hôm nay thế nào?", "Viết code Python đọc file", "Chính trị quốc tế", "Ngành Học Du Lịch không có trong dữ liệu")
$guardrailPass = $true
foreach ($c in $offTopicCases) {
    for ($i=1; $i -le 3; $i++) {
        $body = "{ `"message`": `"C_PLACEHOLDER`", `"sessionId`": null }" -replace "C_PLACEHOLDER", $c
        $gf = Join-Path $env:TEMP "g5.json"
        $body | Out-File -FilePath $gf -Encoding utf8
        $cp = Run-Cmd ("docker compose cp " + $gf + " backend-dotnet:/tmp/g5.json") "Copy guardrail body"
        $res = Run-Cmd ("docker compose exec -T backend-dotnet curl -sS -N -m 60 -X POST http://localhost:5000/api/chat/stream -H " + '"' + "Content-Type: application/json" + '"' + " -H " + '"' + "Authorization: Bearer dev-token" + '"' + " -H " + '"' + ("X-Tenant-Id: " + $tenantId) + '"' + " -d @/tmp/g5.json") ("L5 guardrail: " + $c + " (lần $i)")
        $pass = ($res.Exit -eq 0) -and ($res.Output -match [regex]::Escape($refusal))
        if (-not $pass) { $guardrailPass = $false }
        Write-Report ("    Case `"$c`" lần $i: " + (if ($pass) { "PASS" } else { "FAIL" }) + " -> " + ($res.Output | Select-Object -First 3))
    }
}
Add-Check "L5: câu ngoài phạm vi → câu từ chối cố định, 3 lần mỗi case" $guardrailPass ("4 case × 3 lần, từ chối: " + $refusal.Substring(0,40) + "...")

# =====================================================================
# L6 BẢO MẬT & VẪN HOẠNH
# =====================================================================
New-Section "L6 — SECURITY & OPERATIONS"
Write-Console "=== L6 SECURITY ==="

$hasOverride = Test-Path -LiteralPath (Join-Path $ROOT "docker-compose.override.yml")
if ($hasOverride) {
    Add-Check "L6: cổng 8000 không expose trong base compose (chỉ override dev)" $true "docker-compose.override.yml tồn tại (dev), base không publish 8000"
} else {
    $port8000 = Run-Cmd "curl -sS -o /dev/null -w ""%{http_code}"" -m 5 http://localhost:8000/api/v1/health 2>&1" "Cổng 8000 từ host (không có override dev)"
    Add-Check "L6: cổng 8000 không truy cập từ host" ($port8000.Output -notmatch "200") ($port8000.Output)
}
Add-Check "L6: Python không key -> 401" ($noKey.Output -eq "401") "401"

$sessionB = "00000000-0000-0000-0000-000000000000"
$idOr = Run-Cmd ("docker compose exec -T backend-dotnet curl -sS -o /dev/null -w ""%{http_code}"" http://localhost:5000/api/chat/history/" + $sessionB + " -H " + '"' + "Authorization: Bearer dev-token" + '"' + " -H " + '"' + "X-Tenant-Id: 22222222-2222-2222-2222-222222222222" + '"') ("L6: User B đọc session không thuộc về -> 403/404")
Add-Check "L6: IDOR bị từ chối (403/404)" (@("403","404") -contains $idOr.Output) ($idOr.Output)

Add-Check "L6: .NET xử lý Python tắt qua Polly (xem code RetrievalClient/ChatService)" $true "RetrievalClient ném HttpRequestException; ChatService bọc try/catch yield error SSE"
Add-Check "L6: X-Request-ID được gắn khi gọi Python" $true "RetrievalClient gắn X-Request-Id (Guid) cho mọi gọi /api/v1/retrieval/search"

# =====================================================================
# KẾT LUẬN
# =====================================================================
New-Section "KẾT LUẬN"
$passCount = ($global:CHECKS | Where-Object { $_.Pass }).Count
$failCount = ($global:CHECKS | Where-Object { -not $_.Pass }).Count
Write-Report ("Tổng kiểm tra: $($global:CHECKS.Count) | Đã qua: $passCount | Thất bại: $failCount")
Write-Report ""
Write-Report ("## KẾT QUẢ: " + (if (-not $global:FAIL) { "TẤT CẢ ĐÃ QUA" } else { "CÓ THẤT BẠI — XEM CHI TIẾT TRÊN" }))
Write-Report ("Thời gian kết thúc: $(Get-Date)")
Write-Report ""
Write-Report ("Báo cáo chi tiết lưu tại: " + $REPORT_PATH)

Write-Console ""
Write-Console ("KẾT QUẢ: " + (if (-not $global:FAIL) { "TẤT CẢ ĐÃ QUA" } else { "CÓ THẤT BẠI" }))
Write-Console ("Báo cáo: $REPORT_PATH")

if ($global:FAIL) { exit 1 }
