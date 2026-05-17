using Microsoft.AspNetCore.Mvc;
using TradeX.Core.ErrorCodes;

namespace TradeX.Api.Controllers;

public static class ControllerExtensions
{
    public static IActionResult Error(this ControllerBase controller, BusinessErrorCode code, string message, int httpStatus = 400)
    {
        return controller.StatusCode(httpStatus, new ErrorResponse(code, message, controller.HttpContext.TraceIdentifier));
    }

    public static IActionResult BadRequest(this ControllerBase controller, BusinessErrorCode code, string message)
        => controller.Error(code, message, 400);

    public static IActionResult NotFound(this ControllerBase controller, BusinessErrorCode code, string message)
        => controller.Error(code, message, 404);

    public static IActionResult Conflict(this ControllerBase controller, BusinessErrorCode code, string message)
        => controller.Error(code, message, 409);

    public static IActionResult Unauthorized(this ControllerBase controller, BusinessErrorCode code, string message)
        => controller.Error(code, message, 401);

    public static IActionResult Forbidden(this ControllerBase controller, BusinessErrorCode code, string message)
        => controller.Error(code, message, 403);
}
