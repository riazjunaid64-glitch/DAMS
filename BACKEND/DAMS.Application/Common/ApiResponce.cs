namespace DAMS.Application.Common
{
    public class ApiResponse<T>
    {
        public bool Success { get; set; }
        public string Message { get; set; } = string.Empty;
        public T? Data { get; set; }
        public static ApiResponse<T> SuccessResponse(T data, string message = "")
            => new() { Success = true, Message = message, Data = data };
        public static ApiResponse<T> FailResponse(string message)
            => new() { Success = false, Message = message };
    }
}
