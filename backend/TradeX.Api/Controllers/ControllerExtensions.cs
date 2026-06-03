using Microsoft.AspNetCore.Mvc;

namespace TradeX.Api.Controllers;

public static class ControllerExtensions
{
    public static IActionResult Error(this ControllerBase controller, string message, int httpStatus)
    {
        return controller.StatusCode(httpStatus, new { message, controller.HttpContext.TraceIdentifier });
    }

    public static IActionResult BadRequest(this ControllerBase controller, string message)
        => controller.Error(message, 400);

    public static IActionResult NotFound(this ControllerBase controller, string message)
        => controller.Error(message, 404);

    public static IActionResult Conflict(this ControllerBase controller, string message)
        => controller.Error(message, 409);

    public static IActionResult Unauthorized(this ControllerBase controller, string message)
        => controller.Error(message, 401);

    public static IActionResult Forbidden(this ControllerBase controller, string message)
        => controller.Error(message, 403);
}
