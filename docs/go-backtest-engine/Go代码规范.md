# Go 语言代码规范

## 概述

- **版本**：v2.0
- **日期**：2026-06-05
- **作者**：architect
- **适用范围**：所有 Go 后端服务项目

---

## 1. 项目结构

遵循 [Standard Go Project Layout](https://github.com/golang-standards/project-layout)，各项目可根据自身架构选型进一步细化。以下提供两种常见模式作为参考。

### 1.1 基础分层结构（适用于简单项目或快速起步）

```text
project-root/
├── cmd/                           # 可执行程序入口
│   └── app/
│       └── main.go                # 仅调用 internal 层逻辑
├── internal/                      # 私有应用代码，Go 编译器强制外部不可导入
│   ├── cli/                       # 命令行工具定义（如 cobra、urfave/cli）
│   │   └── root.go
│   ├── config/                    # 配置加载
│   │   └── config.go
│   ├── service/                   # 业务逻辑层
│   ├── handler/                   # HTTP 处理器
│   ├── middleware/                # HTTP 中间件
│   ├── repository/                # 数据访问实现
│   ├── model/                     # 数据模型
│   ├── router/                    # 路由注册
│   ├── server/                    # HTTP 服务器初始化与启动
│   └── pkg/                       # 内部共享工具
├── pkg/                           # 可被外部项目导入的共享库（可选）
├── configs/                       # 运行时配置文件
├── scripts/                       # 构建/部署脚本
├── go.mod
├── go.sum
├── Makefile
└── Dockerfile
```

### 1.2 DDD 分层架构（适用于复杂业务领域）

采用领域驱动设计（DDD）时，`internal/` 按**四层架构**组织：

```text
project-root/
├── cmd/                           # 可执行程序入口
│   └── app/
│       └── main.go
├── internal/
│   ├── cli/                       # 命令行工具（可选，无 server 入口时不可用）
│   ├── config/                    # 配置加载
│   │
│   ├── domain/                    # ── 领域层 ── 核心业务与接口定义
│   │   ├── user/                  # 每个领域一个子包
│   │   │   ├── entity.go          # 领域实体
│   │   │   ├── errors.go          # 领域错误
│   │   │   └── repository.go      # 仓储接口
│   │   ├── order/                 # 其他领域…
│   │   └── payment/
│   │
│   ├── infra/                     # ── 基础设施层 ── 领域接口实现与外部集成
│   │   ├── repository/            # 领域仓储的 ORM/DB 实现
│   │   ├── crypto/                # 加解密
│   │   ├── jwt/                   # JWT 令牌
│   │   ├── sms/                   # 短信
│   │   └── payment/               # 支付渠道
│   │
│   ├── server/                    # ── 接口层 ── HTTP 服务器与用例编排
│   │   ├── <endpoint>/             # 按端（BFF）划分，如 admin / customer
│   │   │   ├── api/                # HTTP 接口层
│   │   │   │   ├── handler/        # HTTP 处理器
│   │   │   │   ├── middleware/     # HTTP 中间件
│   │   │   │   └── router/         # 路由注册
│   │   │   └── app/                # 应用层：用例编排
│   │   │       └── <biz>/          # 按业务领域划分，如 auth / order / user
│   │
│   └── pkg/                       # 内部共享工具
├── pkg/                           # 可导出的共享库（可选）
├── configs/
├── go.mod
└── go.sum
```

### 1.3 目录职责

| 目录                       | 层级        | 外部可导入 | 说明                                           |
| ------------------------ | --------- | ----- | -------------------------------------------- |
| `cmd/`                   | —         | ❌     | 可执行程序入口，每个子目录对应一个 main.go                    |
| `internal/config/`       | 配置层       | ❌     | 配置加载与解析                                      |
| `internal/domain/`       | **领域层**   | ❌     | 领域实体（Entity）、仓储接口（Repository）、领域错误（DDD 模式引入） |
| `internal/infra/`        | **基础设施层** | ❌     | 领域接口的持久化/外部服务实现（DDD 模式引入）                    |
| `internal/server/*/api/` | **接口层**   | ❌     | HTTP 处理器 + 中间件 + 路由（DDD 模式中按端组织）             |
| `internal/server/*/app/` | **应用层**   | ❌     | 用例编排，协调 domain + infra 完成业务（DDD 模式引入）        |
| `internal/service/`      | 业务层       | ❌     | 业务逻辑编排（简单分层模式）                               |
| `internal/handler/`      | 接口层       | ❌     | HTTP 请求处理器（简单分层模式）                           |
| `internal/middleware/`   | 接口层       | ❌     | HTTP 中间件                                     |
| `internal/repository/`   | 数据层       | ❌     | 数据访问实现（简单分层模式）                               |
| `internal/pkg/`          | 共享层       | ❌     | 仅供 internal 各层共享的工具代码                        |
| `pkg/`                   | 共享层       | ✅     | 可被外部项目导入的共享库                                 |
| `configs/`               | —         | ❌     | 运行时配置文件，不参与编译                                |

> **注意**：`internal/` 下的目录组织没有绝对标准，以上为通用参考。关键在于保持依赖方向一致——**依赖倒置**：高层模块不依赖低层模块，双方都依赖抽象（接口）。

---

## 2. 命名规范

### 2.1 包名

- 全小写，单数形式
- 简洁、自描述，无需包含下划线或混合大小写

```go
package service
package handler
package middleware
```

### 2.2 文件命名

- 小写 + 下划线
- 按职责划分，避免单个文件过长

```text
game_service.go
api_key_handler.go
auth_middleware.go
```

### 2.3 标识符命名

| 元素    | 规范             | 示例                                |
| ----- | -------------- | --------------------------------- |
| 变量    | 驼峰，短名称         | `userID`, `svc`, `cfg`            |
| 常量    | 驼峰（Go 惯例，非全大写） | `MaxRetryCount`, `DefaultTimeout` |
| 结构体   | 导出驼峰           | `GameService`, `CreateRequest`    |
| 接口    | 导出驼峰，加 `er` 后缀 | `UserRepository`, `Validator`     |
| 方法/函数 | 导出驼峰 / 私有驼峰    | `GetByID()`, `validateRequest()`  |
| 接收者   | 单字母或简短缩写       | `s *GameService`, `h *Handler`    |
| 错误变量  | 以 `Err` 开头     | `ErrNotFound`, `ErrInvalidInput`  |
| 测试函数  | `TestXxx`      | `TestGameService_GetByID`         |

### 2.4 包导入别名

当包名与路径末尾不一致时，必须使用别名：

```go
import (
    "context"
    "time"

    "github.com/gin-gonic/gin"
    "github.com/redis/go-redis/v9"

    "your-module/internal/handler"  // 无需别名
    dto "your-module/internal/dto"  // 仅当与常见名称冲突时使用
)
```

---

## 3. 代码格式与风格

### 3.1 强制规则

- 使用 `go fmt` 格式化代码，提交前必须格式化
- 每行长度不超过 120 字符
- 使用 `go vet` 静态检查，零警告通过
- 导入使用标准分组顺序：标准库 → 第三方 → 本地包

```go
import (
    "context"
    "fmt"
    "time"

    "github.com/gin-gonic/gin"
    "github.com/redis/go-redis/v9"

    "your-module/internal/config"
    "your-module/internal/handler"
)
```

### 3.2 声明风格

```go
// 变量声明：就近原则
var count int
msg := "hello"

// 结构体初始化：多字段使用多行
user := User{
    Name:  "Alice",
    Email: "alice@example.com",
}

// 错误处理：立即检查
if err := doSomething(); err != nil {
    return fmt.Errorf("do something: %w", err)
}
```

### 3.3 函数定义

- 返回值命名：仅当文档需要或返回同类型多个值时使用
- 错误总是最后一个返回值

```go
// 好的做法
func (s *Service) GetByID(ctx context.Context, id int64) (*User, error) {
    // ...
}

// 避免：不命名的多个同类型返回值
func parse(input string) (int, int, error) {
    // ...
}
```

---

## 4. 错误处理

### 4.1 错误包装

始终使用 `fmt.Errorf` + `%w` 包装错误，保留错误链：

```go
if err := repo.Find(ctx, id); err != nil {
    return nil, fmt.Errorf("find user %d: %w", id, ErrNotFound)
}
```

### 4.2 自定义错误类型

```go
var (
    ErrNotFound     = errors.New("resource not found")
    ErrConflict     = errors.New("resource already exists")
    ErrUnauthorized = errors.New("unauthorized")
    ErrForbidden    = errors.New("forbidden")
    ErrInvalidInput = errors.New("invalid input")
)
```

### 4.3 业务错误 vs 系统错误

| 错误类型 | 处理方式                   |
| ---- | ---------------------- |
| 业务错误 | 转为业务错误码返回给调用方          |
| 系统错误 | 记录日志，返回通用错误响应，避免暴露内部细节 |

### 4.4 统一响应格式（HTTP 服务）

```go
type Response struct {
    Code    int         `json:"code"`    // 0 表示成功，非 0 表示错误码
    Message string      `json:"message"` // 成功时固定值，失败时为错误描述
    Data    interface{} `json:"data"`    // 实际数据
}
```

---

## 5. HTTP Handler 规范

### 5.1 Handler 职责

- 仅做请求解析、参数校验、调用 Service、返回响应
- 不包含任何业务逻辑

```go
type Handler struct {
    svc *Service
}

func NewHandler(svc *Service) *Handler {
    return &Handler{svc: svc}
}

// Create 创建资源
func (h *Handler) Create(ctx context.Context, req *CreateRequest) (*CreateResponse, error) {
    // 参数校验
    if err := validate(req); err != nil {
        return nil, fmt.Errorf("validate: %w", err)
    }
    // 调用业务层
    return h.svc.Create(ctx, req)
}
```

### 5.2 错误处理

- Handler 中不直接处理错误，通过中间件统一拦截并转换为响应
- 错误由 Service 层通过 sentinel error 或自定义错误类型抛出

---

## 6. Service 业务逻辑层

### 6.1 分层依赖

```
Handler → Service → Repository
```

### 6.2 Service 规范

- 接收 `context.Context` 作为第一个参数
- 返回 `(T, error)` 或 `error`
- 一个 Service 对应一个领域聚合（DDD 场景）或一个业务模块

```go
type UserService struct {
    repo UserRepository
}

func NewUserService(repo UserRepository) *UserService {
    return &UserService{repo: repo}
}

func (s *UserService) Create(ctx context.Context, req *CreateUserRequest) (*UserResponse, error) {
    if err := s.validateCreate(req); err != nil {
        return nil, fmt.Errorf("validate: %w", err)
    }
    entity, err := s.repo.Create(ctx, req.ToEntity())
    if err != nil {
        return nil, fmt.Errorf("create user: %w", err)
    }
    return toDTO(entity), nil
}
```

---

## 7. 数据访问层

### 7.1 接口隔离

- 数据访问抽象为接口，定义在调用方所在包（domain 或 service）
- 实现类通过依赖注入注入到业务层

```go
// domain 层定义接口
type UserRepository interface {
    FindByID(ctx context.Context, id int64) (*User, error)
    Create(ctx context.Context, user *User) error
}

// infra 层实现
type userRepository struct {
    db *sql.DB  // 或 ORM client
}

func NewUserRepository(db *sql.DB) UserRepository {
    return &userRepository{db: db}
}
```

### 7.2 查询规范

- 列表查询必须分页，避免一次性加载大量数据
- 复杂查询优先使用 ORM 提供的查询构建器，避免拼接 SQL
- 涉及跨表关联的写操作必须使用事务

### 7.3 事务管理

- 跨越多个实体的写操作必须使用事务
- 推荐使用 `Tx` 方法封装事务边界，避免手动 commit/rollback 散落各处

```go
func (s *Service) Transfer(ctx context.Context, fromID, toID int64, amount float64) error {
    return s.repo.WithinTransaction(ctx, func(tx RepositoryTx) error {
        if err := tx.DeductBalance(ctx, fromID, amount); err != nil {
            return err
        }
        return tx.AddBalance(ctx, toID, amount)
    })
}
```

---

## 8. 中间件规范

### 8.1 通用设计原则

- 中间件通过责任链模式串联，每个中间件只关注单一横切关注点
- 中间件执行顺序：全局 → 路由级 → 组级

### 8.2 典型中间件列表

| 中间件       | 用途                  | 推荐范围  |
| --------- | ------------------- | ----- |
| Recovery  | Panic 恢复，返回 500     | 全局    |
| CORS      | 跨域支持                | 全局    |
| Logger    | 请求日志（方法、路径、状态码、耗时）  | 全局    |
| TraceID   | 链路追踪 ID 注入          | 全局    |
| Auth      | 身份认证（JWT / Session） | 受保护路由 |
| RateLimit | 限流                  | 敏感路由  |

---

## 9. 配置管理

- 配置与代码分离，使用配置库（如 Viper、envconfig）或标准库 `os.Getenv` 加载
- 配置文件推荐使用 YAML 格式，配合环境变量覆盖
- 敏感信息（数据库密码、密钥）通过环境变量注入，不写入版本控制

```go
type Config struct {
    Server   ServerConfig   `mapstructure:"server"`
    Database DatabaseConfig `mapstructure:"database"`
}

type ServerConfig struct {
    Port         int `mapstructure:"port"`
    ReadTimeout  int `mapstructure:"read_timeout"`
    WriteTimeout int `mapstructure:"write_timeout"`
}

type DatabaseConfig struct {
    DSN string `mapstructure:"dsn"`  // 从环境变量读取
}
```

---

## 10. 数据库规范

### 10.1 迁移管理

- 开发环境使用 ORM 自动迁移
- 生产环境禁止使用自动迁移，必须使用版本化迁移

### 10.2 索引规范

- 所有外键字段必须建索引
- 常用查询条件的字段建索引
- 联合索引注意字段顺序（等值条件在前，范围条件在后）

### 10.3 敏感数据存储

- 密码使用 `bcrypt` 或 `argon2` 哈希存储
- 密钥等敏感信息使用 `AES-256-GCM` 或等价算法加密存储
- 加密密钥从环境变量读取，禁止硬编码

---

## 11. 日志规范

- 使用结构化日志（推荐标准库 `log/slog`，或 `zerolog`、`zap`）
- 日志记录必须包含：时间戳、级别、请求 ID（若有）、操作、耗时
- 敏感信息（密码、令牌、密钥）禁止输出

```go
slog.Info("user login success",
    "request_id", requestID,
    "user_id", userID,
    "duration_ms", elapsed,
)
```

---

## 12. 测试规范

### 12.1 测试层级

| 层级     | 覆盖范围                         |
| ------ | ---------------------------- |
| 单元测试   | Service / 业务逻辑               |
| 集成测试   | 跨层交互（Handler + Service + DB） |
| API 测试 | 完整 HTTP 端点                   |

### 12.2 单元测试要求

- Service 层测试覆盖率 ≥ 80%
- 使用 mock 或 stub 隔离外部依赖（数据库、网络、缓存）
- 测试命名：`Test{ServiceName}_{MethodName}`

```go
func TestUserService_Create(t *testing.T) {
    t.Parallel()
    // Arrange
    svc := setupService(t)
    req := &CreateUserRequest{
        Email:    "test@example.com",
        Password: "SecurePass123!",
    }
    // Act
    user, err := svc.Create(context.Background(), req)
    // Assert
    assert.NoError(t, err)
    assert.NotEmpty(t, user.ID)
    assert.Equal(t, req.Email, user.Email)
}
```

### 12.3 表驱动测试

对多场景测试使用表驱动模式：

```go
func TestValidateEmail(t *testing.T) {
    tests := []struct {
        name  string
        email string
        want  bool
    }{
        {"valid email", "user@example.com", true},
        {"missing @", "userexample.com", false},
        {"empty", "", false},
    }
    for _, tt := range tests {
        t.Run(tt.name, func(t *testing.T) {
            assert.Equal(t, tt.want, validateEmail(tt.email))
        })
    }
}
```

---

## 13. 安全规范

| 类别      | 规范                               |
| ------- | -------------------------------- |
| SQL 注入  | 禁止拼接 SQL，始终使用 ORM 或参数化查询         |
| XSS     | 所有用户输入必须做输出转义                    |
| CSRF    | API 使用 Token 认证（无需额外 CSRF Token） |
| 密码      | 使用 bcrypt cost ≥ 10 或 argon2     |
| JWT     | 使用 HS256/RS256 签名，过期时间 ≤ 7 天     |
| API Key | 加密存储（AES-256-GCM 或等价算法），仅创建时明文展示 |
| HTTPS   | 生产环境强制 HTTPS                     |
| 输入校验    | 所有用户输入做类型、长度、格式校验                |

---

## 14. 并发与性能

- 使用 `errgroup` 管理并发 Goroutine
- 数据库查询注意 N+1 问题，使用 eager loading
- 使用连接池管理数据库连接，合理配置最大连接数和空闲连接数
- API 响应时间目标根据业务特性自行定义

```go
g, ctx := errgroup.WithContext(ctx)
g.Go(func() error {
    return svc.processBatch(ctx, batch1)
})
g.Go(func() error {
    return svc.processBatch(ctx, batch2)
})
if err := g.Wait(); err != nil {
    return err
}
```

---

## 15. Lint 与工具链

| 工具              | 用途                                        | 频率      |
| --------------- | ----------------------------------------- | ------- |
| `go fmt`        | 代码格式化                                     | 每次提交前   |
| `go vet`        | 静态分析                                      | 每次提交前   |
| `golangci-lint` | 综合 Lint（含 govet, staticcheck, errcheck 等） | CI      |
| `go mod tidy`   | 依赖清理                                      | 每次依赖变更后 |

### Makefile 参考模板

```makefile
.PHONY: fmt vet lint test build

fmt:
    go fmt ./...

vet:
    go vet ./...

lint:
    golangci-lint run ./... --timeout=3m

test:
    go test -race -count=1 -coverprofile=coverage.out ./...

build:
    go build -o bin/app ./cmd/app
```

---

## 16. 编码原则总结

1. **单一职责**：一个包、一个文件、一个函数只做一件事
2. **显式错误处理**：不忽略任何错误返回值
3. **依赖注入**：Handler/Service 的依赖通过构造函数注入，不使用全局变量
4. **接口隔离**：定义小而精的接口，`interface{}` 只含必要方法
5. **零值可用**：利用 Go 零值语义设计，减少初始化代码
6. **并发安全**：共享变量使用互斥锁或 Channel，避免竞态
7. **尽早返回**：减少嵌套，错误尽早 return

---

> 本文档作为通用 Go 编码规范，各项目可在不违反基本原则的前提下，根据自身技术栈和架构选型补充项目级细化规范。
