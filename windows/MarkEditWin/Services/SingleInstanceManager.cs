using System.IO;
using System.IO.Pipes;
using System.Text;

namespace MarkEditWin.Services;

/// <summary>
/// Keeps MarkEdit to a single process, because a WebView2 user data folder cannot be shared by
/// several browser processes. Secondary launches forward their file argument to the running
/// instance, which opens another window for it.
/// </summary>
public static class SingleInstanceManager
{
    private const string MutexName = @"Local\MarkEdit.SingleInstance.v1";
    private const string PipeName = "MarkEdit.SingleInstance.Pipe.v1";
    private const int MaxPayloadLength = 4096;

    private static Mutex? _mutex;

    public static bool IsFirstInstance { get; private set; }

    /// <summary>Raised in the first instance with the file path sent by a secondary launch.</summary>
    public static event Action<string?>? LaunchRequested;

    public static void Initialize()
    {
        _mutex = new Mutex(initiallyOwned: true, MutexName, out var createdNew);
        IsFirstInstance = createdNew;

        if (createdNew)
        {
            StartPipeServer();
        }
    }

    /// <summary>Hands the arguments to the running instance, returns false when it could not be reached.</summary>
    public static bool ForwardToRunningInstance(string? path)
    {
        try
        {
            using var client = new NamedPipeClientStream(".", PipeName, PipeDirection.Out);
            client.Connect(3000);

            using var writer = new StreamWriter(client, Encoding.UTF8) { AutoFlush = true };
            writer.WriteLine(path ?? string.Empty);
            return true;
        }
        catch (Exception)
        {
            return false;
        }
    }

    private static void StartPipeServer()
    {
        var thread = new Thread(() =>
        {
            while (true)
            {
                try
                {
                    using var server = new NamedPipeServerStream(
                        PipeName,
                        PipeDirection.In,
                        maxNumberOfServerInstances: 1,
                        PipeTransmissionMode.Byte,
                        PipeOptions.None);

                    server.WaitForConnection();

                    using var reader = new StreamReader(server, Encoding.UTF8);
                    var payload = reader.ReadLine();
                    if (payload is null || payload.Length > MaxPayloadLength)
                    {
                        continue;
                    }

                    LaunchRequested?.Invoke(payload.Length == 0 ? null : payload);
                }
                catch (Exception)
                {
                    // A broken client must not stop the listener, keep serving.
                    Thread.Sleep(200);
                }
            }
        })
        {
            IsBackground = true,
            Name = "MarkEdit Single Instance Listener",
        };

        thread.Start();
    }
}
