using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using TradeX.Api.Filters;
using TradeX.Application.Common;
using TradeX.Application.Users;
using TradeX.Application.Users.DTOs;

namespace TradeX.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
[Authorize]
public class UsersController(
    IUseCase<GetUsersQuery, Result<List<UserDto>>> getUsersUseCase,
    IUseCase<GetUserByIdQuery, Result<UserDto>> getUserByIdUseCase,
    IUseCase<UpdateUserRoleCommand, Result> updateUserRoleUseCase) : ControllerBase
{
    [HttpGet]
    public async Task<IActionResult> GetAll(CancellationToken ct)
    {
        var result = await getUsersUseCase.ExecuteAsync(new GetUsersQuery(), ct);
        return Ok(result.Data);
    }

    [HttpGet("{id:guid}")]
    public async Task<IActionResult> GetById(Guid id, CancellationToken ct)
    {
        var result = await getUserByIdUseCase.ExecuteAsync(new GetUserByIdQuery(id), ct);
        if (!result.Success)
            return NotFound(new { message = result.Error });

        return Ok(result.Data);
    }

    [HttpPut("{id:guid}/role")]
    [RequireMfa]
    public async Task<IActionResult> UpdateRole(Guid id, [FromBody] UpdateRoleRequest request, CancellationToken ct)
    {
        var result = await updateUserRoleUseCase.ExecuteAsync(new UpdateUserRoleCommand(id, request.Role), ct);
        if (!result.Success)
            return result.StatusCode switch
            {
                404 => NotFound(new { message = result.Error }),
                400 => BadRequest(new { message = result.Error }),
                _ => BadRequest(new { message = result.Error })
            };

        return Ok(new { message = "角色已更新" });
    }

    public record UpdateRoleRequest(string Role);
}
