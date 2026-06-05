using TradeX.Application.Common;
using TradeX.Application.Users.DTOs;
using TradeX.Core.Enums;
using TradeX.Core.Interfaces;

namespace TradeX.Application.Users;

public sealed record GetUsersQuery;

public sealed class GetUsersUseCase(IUserRepository userRepo)
    : IUseCase<GetUsersQuery, Result<List<UserDto>>>
{
    public async Task<Result<List<UserDto>>> ExecuteAsync(GetUsersQuery query, CancellationToken ct = default)
    {
        var users = await userRepo.GetAllAsync(ct);
        var dtos = users.Select(u => new UserDto(
            u.Id, u.Username, u.Email,
            u.Role.ToString(), u.Status.ToString(),
            u.CreatedAt.ToString("yyyy-MM-dd HH:mm:ss"), u.LastLoginAt)).ToList();

        return Result<List<UserDto>>.Ok(dtos);
    }
}

public sealed record GetUserByIdQuery(Guid Id);

public sealed class GetUserByIdUseCase(IUserRepository userRepo)
    : IUseCase<GetUserByIdQuery, Result<UserDto>>
{
    public async Task<Result<UserDto>> ExecuteAsync(GetUserByIdQuery query, CancellationToken ct = default)
    {
        var user = await userRepo.GetByIdAsync(query.Id, ct);
        if (user is null)
            return Result<UserDto>.NotFound("用户不存在");

        return Result<UserDto>.Ok(new UserDto(
            user.Id, user.Username, user.Email,
            user.Role.ToString(), user.Status.ToString(),
            user.CreatedAt.ToString("yyyy-MM-dd HH:mm:ss"), user.LastLoginAt));
    }
}

public sealed record UpdateUserCommand(Guid UserId, string Username, string Role);

public sealed class UpdateUserUseCase(IUserRepository userRepo)
    : IUseCase<UpdateUserCommand, Result>
{
    public async Task<Result> ExecuteAsync(UpdateUserCommand cmd, CancellationToken ct = default)
    {
        var user = await userRepo.GetByIdAsync(cmd.UserId, ct);
        if (user is null)
            return Result.NotFound("用户不存在");

        if (user.Role == UserRole.SuperAdmin)
            return Result.BadRequest("不能修改 SuperAdmin");

        if (!string.IsNullOrWhiteSpace(cmd.Username))
            user.Username = cmd.Username;

        if (!string.IsNullOrWhiteSpace(cmd.Role) && Enum.TryParse<UserRole>(cmd.Role, true, out var newRole))
            user.Role = newRole;

        await userRepo.UpdateAsync(user, ct);
        return Result.Ok();
    }
}

public sealed record DeleteUserCommand(Guid UserId);

public sealed class DeleteUserUseCase(IUserRepository userRepo)
    : IUseCase<DeleteUserCommand, Result>
{
    public async Task<Result> ExecuteAsync(DeleteUserCommand cmd, CancellationToken ct = default)
    {
        var user = await userRepo.GetByIdAsync(cmd.UserId, ct);
        if (user is null)
            return Result.NotFound("用户不存在");

        if (user.Role == UserRole.SuperAdmin)
            return Result.BadRequest("不能删除 SuperAdmin");

        await userRepo.DeleteAsync(user, ct);
        return Result.Ok();
    }
}

public sealed record UpdateUserRoleCommand(Guid UserId, string Role);

/// <summary>更新用户角色用例。</summary>
public sealed class UpdateUserRoleUseCase(IUserRepository userRepo)
    : IUseCase<UpdateUserRoleCommand, Result>
{
    public async Task<Result> ExecuteAsync(UpdateUserRoleCommand cmd, CancellationToken ct = default)
    {
        var user = await userRepo.GetByIdAsync(cmd.UserId, ct);
        if (user is null)
            return Result.NotFound("用户不存在");

        if (user.Role == UserRole.SuperAdmin)
            return Result.BadRequest("不能修改 SuperAdmin 角色");

        if (!Enum.TryParse<UserRole>(cmd.Role, true, out var newRole))
            return Result.BadRequest("无效角色");

        user.Role = newRole;
        await userRepo.UpdateAsync(user, ct);

        return Result.Ok();
    }
}
