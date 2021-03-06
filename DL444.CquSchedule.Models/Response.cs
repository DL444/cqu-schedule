namespace DL444.CquSchedule.Models
{
    public struct Response<T>
    {
        public Response(bool success, T data, string message)
        {
            Success = success;
            Message = message;
            Data = data;
        }
        public Response(T data) : this(true, data, default) { }
        public Response(string message) : this(false, default, message) { }

        public bool Success { get; set; }
        public string Message { get; set; }
        public T Data { get; set; }
    }
}
