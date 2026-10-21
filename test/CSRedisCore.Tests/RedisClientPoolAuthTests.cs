using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using CSRedis;
using Xunit;

namespace CSRedisCore.Tests;

public class RedisClientPoolAuthTests
{
    [Fact]
    public async Task ConnectsWhenRedisRejectsAuthBecauseNoPasswordIsConfigured()
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        var observed = new List<string>();
        var server = ServeAsync(listener, timeout.Token, observed);
        var redis = new CSRedisClient(
            $"127.0.0.1:{port},password=test-password,preheat=false,testcluster=false,tryit=0");
        var response = false;
        Exception failure = null;

        try
        {
            response = redis.Ping();
        }
        catch (Exception ex)
        {
            failure = ex;
        }
        finally
        {
            redis.Dispose();
            timeout.Cancel();
            listener.Stop();
        }

        await server;
        Assert.Equal(new[] { "AUTH", "AUTH_ERROR_SENT", "PING", "PONG_SENT", "PING", "PONG_SENT" }, observed);
        Assert.Null(failure);
        Assert.True(response);
    }

    private static async Task ServeAsync(TcpListener listener, CancellationToken cancellationToken, List<string> observed)
    {
        try
        {
            using var client = await listener.AcceptTcpClientAsync(cancellationToken);
            var stream = client.GetStream();
            var auth = await ReadCommandAsync(stream, cancellationToken);
            observed.Add(auth[0]);
            Assert.Equal("AUTH", auth[0]);
            await WriteAsync(stream, "-ERR Client sent AUTH, but no password is set\r\n", cancellationToken);
            observed.Add("AUTH_ERROR_SENT");

            // The pool health-checks a new connection before issuing the requested Ping.
            for (var i = 0; i < 2; i++)
            {
                var ping = await ReadCommandAsync(stream, cancellationToken);
                observed.Add(ping[0]);
                Assert.Equal("PING", ping[0]);
                await WriteAsync(stream, "+PONG\r\n", cancellationToken);
                observed.Add("PONG_SENT");
            }
        }
        catch (OperationCanceledException)
        {
        }
        catch (IOException)
        {
        }
        catch (SocketException)
        {
        }
    }

    private static async Task<string[]> ReadCommandAsync(Stream stream, CancellationToken cancellationToken)
    {
        var countLine = await ReadLineAsync(stream, cancellationToken);
        Assert.StartsWith("*", countLine);
        var count = int.Parse(countLine.Substring(1), CultureInfo.InvariantCulture);
        var arguments = new string[count];

        for (var i = 0; i < count; i++)
        {
            var lengthLine = await ReadLineAsync(stream, cancellationToken);
            Assert.StartsWith("$", lengthLine);
            var length = int.Parse(lengthLine.Substring(1), CultureInfo.InvariantCulture);
            var bytes = new byte[length];
            var offset = 0;
            while (offset < bytes.Length)
            {
                var read = await stream.ReadAsync(bytes, offset, bytes.Length - offset, cancellationToken);
                if (read == 0) throw new EndOfStreamException();
                offset += read;
            }
            Assert.Equal(string.Empty, await ReadLineAsync(stream, cancellationToken));
            arguments[i] = Encoding.UTF8.GetString(bytes);
        }

        return arguments;
    }

    private static async Task<string> ReadLineAsync(Stream stream, CancellationToken cancellationToken)
    {
        using var line = new MemoryStream();
        var oneByte = new byte[1];
        while (true)
        {
            var read = await stream.ReadAsync(oneByte, 0, 1, cancellationToken);
            if (read == 0) throw new EndOfStreamException();
            if (oneByte[0] != '\r')
            {
                line.WriteByte(oneByte[0]);
                continue;
            }

            read = await stream.ReadAsync(oneByte, 0, 1, cancellationToken);
            if (read == 0) throw new EndOfStreamException();
            Assert.Equal((byte)'\n', oneByte[0]);
            return Encoding.ASCII.GetString(line.ToArray());
        }
    }

    private static Task WriteAsync(Stream stream, string response, CancellationToken cancellationToken)
    {
        var bytes = Encoding.ASCII.GetBytes(response);
        return stream.WriteAsync(bytes, 0, bytes.Length, cancellationToken);
    }
}
