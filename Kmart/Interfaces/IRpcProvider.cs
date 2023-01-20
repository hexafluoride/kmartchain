using System.Collections.Generic;
using System.Net;
using System.Text.Json;
using System.Threading.Tasks;

namespace Kmart.Interfaces;

public interface IRpcProvider
{
    public IEnumerable<string> Commands { get; }
    public Task ServeRequestAsync(HttpListenerContext context, int id, string method, JsonElement parameters);
}