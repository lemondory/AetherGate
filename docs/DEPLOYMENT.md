# AetherGate — 배포 가이드 (AWS ECS)

> 이 문서는 배포 설계 및 절차를 정리한 것입니다.
> 실제 배포 없이 코드와 설정 파일만 준비된 상태이며,
> AWS 계정 준비 후 아래 절차를 순서대로 따라하면 됩니다.

---

## 전체 배포 아키텍처

```
[GitHub]
    │ push
    ▼
[GitHub Actions CI/CD]
    ├── dotnet test (게임 서버 자동 테스트)
    ├── Docker 이미지 빌드
    └── ECR 푸시 → ECS 배포 트리거
                        │
                        ▼
              [AWS 클라우드]
              ┌─────────────────────────────────────┐
              │                                     │
              │  ALB (Application Load Balancer)    │
              │   ├── :8000 → aethergate-web (ECS)  │
              │   └── :9000 → aethergate-server (ECS│
              │                                     │
              │  ECS Cluster                        │
              │   ├── Task: aethergate-server        │
              │   │    (Fargate, linux/amd64)        │
              │   └── Task: aethergate-web           │
              │        (Fargate, linux/arm64)        │
              │                                     │
              │  RDS PostgreSQL     ElastiCache Redis│
              │  (aethergate DB)    (세션 캐시/Pub)  │
              └─────────────────────────────────────┘
```

---

## 서비스별 AWS 리소스 매핑

| 로컬 (docker-compose) | AWS |
|----------------------|-----|
| aethergate-server 컨테이너 | ECS Fargate Task (linux/amd64) |
| aethergate-web 컨테이너 | ECS Fargate Task (linux/arm64) |
| PostgreSQL 컨테이너 | RDS PostgreSQL 16 |
| Redis 컨테이너 | ElastiCache Redis 7 |
| - | ECR (Docker 이미지 저장소) |
| - | ALB (로드 밸런서) |
| - | Secrets Manager (JWT_SECRET 등) |

---

## GitHub Actions CI/CD 파이프라인

### 파이프라인 흐름

```
main 브랜치 push
    │
    ├── [Job 1] test
    │     └── dotnet test (80개 자동 테스트)
    │
    └── [Job 2] deploy (test 통과 시)
          ├── AWS ECR 로그인
          ├── aethergate-server 이미지 빌드 & 푸시
          ├── aethergate-web 이미지 빌드 & 푸시
          └── ECS 서비스 업데이트 (롤링 배포)
```

### 워크플로우 파일 위치

```
.github/
└── workflows/
    └── deploy.yml
```

### GitHub Secrets 등록 필요 항목

| Secret 이름 | 설명 |
|------------|------|
| `AWS_ACCESS_KEY_ID` | IAM 사용자 액세스 키 |
| `AWS_SECRET_ACCESS_KEY` | IAM 사용자 시크릿 키 |
| `AWS_REGION` | 배포 리전 (예: ap-northeast-2) |
| `ECR_REGISTRY` | ECR 레지스트리 URL |
| `JWT_SECRET` | 게임 서버 ↔ 웹 공유 시크릿 |

---

## ECS Task Definition 구성

### aethergate-server

```json
{
  "family": "aethergate-server",
  "cpu": "512",
  "memory": "1024",
  "networkMode": "awsvpc",
  "requiresCompatibilities": ["FARGATE"],
  "runtimePlatform": {
    "cpuArchitecture": "X86_64",
    "operatingSystemFamily": "LINUX"
  },
  "containerDefinitions": [
    {
      "name": "aethergate-server",
      "image": "<ECR_URL>/aethergate-server:latest",
      "portMappings": [
        { "containerPort": 9000, "protocol": "tcp" },
        { "containerPort": 8090, "protocol": "tcp" }
      ],
      "environment": [
        { "name": "JWT_ISSUER",   "value": "AetherGate" },
        { "name": "JWT_AUDIENCE", "value": "AetherGateClient" }
      ],
      "secrets": [
        { "name": "JWT_SECRET",  "valueFrom": "arn:aws:secretsmanager:...:JWT_SECRET" },
        { "name": "REDIS_URL",   "valueFrom": "arn:aws:secretsmanager:...:REDIS_URL" }
      ],
      "logConfiguration": {
        "logDriver": "awslogs",
        "options": {
          "awslogs-group": "/ecs/aethergate-server",
          "awslogs-region": "ap-northeast-2",
          "awslogs-stream-prefix": "ecs"
        }
      }
    }
  ]
}
```

### aethergate-web

```json
{
  "family": "aethergate-web",
  "cpu": "256",
  "memory": "512",
  "networkMode": "awsvpc",
  "requiresCompatibilities": ["FARGATE"],
  "runtimePlatform": {
    "cpuArchitecture": "ARM64",
    "operatingSystemFamily": "LINUX"
  },
  "containerDefinitions": [
    {
      "name": "aethergate-web",
      "image": "<ECR_URL>/aethergate-web:latest",
      "portMappings": [
        { "containerPort": 8000, "protocol": "tcp" }
      ],
      "environment": [
        { "name": "JWT_ISSUER",   "value": "AetherGate" },
        { "name": "JWT_AUDIENCE", "value": "AetherGateClient" },
        { "name": "BASE_URL",     "value": "https://<도메인>" },
        { "name": "GUEST_ENABLED","value": "true" }
      ],
      "secrets": [
        { "name": "JWT_SECRET",    "valueFrom": "arn:aws:secretsmanager:...:JWT_SECRET" },
        { "name": "DATABASE_URL",  "valueFrom": "arn:aws:secretsmanager:...:DATABASE_URL" },
        { "name": "REDIS_URL",     "valueFrom": "arn:aws:secretsmanager:...:REDIS_URL" }
      ],
      "logConfiguration": {
        "logDriver": "awslogs",
        "options": {
          "awslogs-group": "/ecs/aethergate-web",
          "awslogs-region": "ap-northeast-2",
          "awslogs-stream-prefix": "ecs"
        }
      }
    }
  ]
}
```

---

## 실제 배포 체크리스트

### 1단계 — AWS 리소스 준비

```
□ AWS 계정 생성
□ IAM 사용자 생성 (ECS, ECR, Secrets Manager 권한)
□ ECR 레포지토리 2개 생성
    - aethergate-server
    - aethergate-web
□ VPC / 서브넷 / 보안그룹 설정
    - 인바운드: 9000 (TCP, 게임 클라이언트), 8000 (HTTP)
    - 내부: 5432 (PostgreSQL), 6379 (Redis)
□ RDS PostgreSQL 16 생성
    - DB명: aethergate
    - 보안그룹: ECS Task에서만 접근
□ ElastiCache Redis 7 생성
    - 보안그룹: ECS Task에서만 접근
□ Secrets Manager에 시크릿 등록
    - JWT_SECRET, DATABASE_URL, REDIS_URL
```

### 2단계 — ECS 클러스터 구성

```
□ ECS 클러스터 생성 (Fargate)
□ Task Definition 등록
    - aethergate-server (X86_64)
    - aethergate-web (ARM64)
□ ECS 서비스 생성
    - aethergate-server: 최소 1 태스크
    - aethergate-web: 최소 1 태스크
□ ALB 생성 및 타겟 그룹 연결
```

### 3단계 — CI/CD 연결

```
□ GitHub Secrets 등록 (위 목록 참고)
□ main 브랜치에 push → 자동 배포 확인
□ 헬스체크 URL 확인
    - http://<ALB주소>:8000/docs
    - telnet <ALB주소> 9000
```

---

## 예상 비용 (서울 리전 기준, 월)

| 서비스 | 사양 | 예상 비용 |
|--------|------|-----------|
| ECS Fargate (server) | 0.5 vCPU / 1GB | ~$15 |
| ECS Fargate (web) | 0.25 vCPU / 0.5GB | ~$8 |
| RDS PostgreSQL | db.t3.micro | ~$15 |
| ElastiCache Redis | cache.t3.micro | ~$15 |
| ALB | - | ~$20 |
| ECR | 이미지 저장 | ~$1 |
| **합계** | | **~$74/월** |

> 프리티어 계정 첫 12개월은 일부 항목 무료.
> 테스트 후 중지하면 ECS/RDS는 비용 미발생.

---

## 로컬 → 배포 환경 차이점

| 항목 | 로컬 (docker-compose) | AWS (ECS) |
|------|-----------------------|-----------|
| DB | PostgreSQL 컨테이너 | RDS (관리형) |
| Redis | Redis 컨테이너 | ElastiCache (관리형) |
| 시크릿 | `.env` 파일 | Secrets Manager |
| 로그 | docker logs | CloudWatch Logs |
| 스케일링 | 수동 | ECS Auto Scaling |
| TLS | 없음 | ALB + ACM 인증서 |
