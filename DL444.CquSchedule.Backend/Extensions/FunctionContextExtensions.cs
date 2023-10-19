using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Logging;

namespace DL444.CquSchedule.Backend.Extensions
{
    internal static class FunctionContextExtensions
    {
        public static ILogger GetFunctionNamedLogger(this FunctionContext ctx) => ctx.GetLogger(ctx.FunctionDefinition.Name);
    }
}
