namespace TradeX.Core.ErrorCodes;

public record ErrorResponse(BusinessErrorCode Code, string Message, string TraceId);

public record DataResponse<T>(T Data);

public record ListResponse<T>(T[] Data, int Total);
