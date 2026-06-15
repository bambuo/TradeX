using Microsoft.AspNetCore.Mvc;
using TradeX.Core.ErrorCodes;

namespace TradeX.Api.Controllers;

public static class ControllerExtensions
{
    private static IActionResult Error(this ControllerBase controller, string message, int httpStatus, BusinessErrorCode code = BusinessErrorCode.ValidationError)
    {
        return controller.StatusCode(httpStatus, ApiResponse.Error(code, message));
    }

    public static IActionResult BadRequest(this ControllerBase controller, string message, BusinessErrorCode code = BusinessErrorCode.ValidationError)
        => controller.Error(message, 400, code);

    public static IActionResult NotFound(this ControllerBase controller, string message, BusinessErrorCode code = BusinessErrorCode.NotFound)
        => controller.Error(message, 404, code);

    public static IActionResult Conflict(this ControllerBase controller, string message, BusinessErrorCode code = BusinessErrorCode.Conflict)
        => controller.Error(message, 409, code);

    public static IActionResult Unauthorized(this ControllerBase controller, string message, BusinessErrorCode code = BusinessErrorCode.Unauthenticated)
        => controller.Error(message, 401, code);

    public static IActionResult Forbidden(this ControllerBase controller, string message, BusinessErrorCode code = BusinessErrorCode.Forbidden)
        => controller.Error(message, 403, code);
}
