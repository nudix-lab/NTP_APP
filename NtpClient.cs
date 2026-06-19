using System.Diagnostics;
using System.Net;
using System.Net.Sockets;

namespace NtpTimer;

/// <summary>
/// 최소 SNTP(v3) 클라이언트. UDP 123 으로 NTP 서버에 1회 질의하여
/// 로컬 고해상도 시계(Stopwatch) 대비 "참 UTC" 앵커를 구한다.
/// 왕복지연을 4-타임스탬프 공식으로 보정한다.
/// </summary>
public static class NtpClient
{
    // NTP 타임스탬프 기준(1900-01-01) → .NET DateTime 변환용
    private static readonly DateTime NtpEpoch = new(1900, 1, 1, 0, 0, 0, DateTimeKind.Utc);

    public readonly struct Result
    {
        /// <summary>응답 수신 순간(t4)에 대응하는 보정된 UTC.</summary>
        public DateTime AnchorUtc { get; init; }
        /// <summary>AnchorUtc 를 찍은 순간의 Stopwatch 타임스탬프(틱).</summary>
        public long AnchorTicks { get; init; }
        /// <summary>로컬시계와 NTP의 오프셋(NTP - 로컬). 진단용.</summary>
        public TimeSpan Offset { get; init; }
        /// <summary>왕복지연. 진단용.</summary>
        public TimeSpan RoundTrip { get; init; }
        public string Server { get; init; }
    }

    /// <summary>
    /// 지정한 서버들을 순서대로 시도하여 첫 성공 결과를 반환한다.
    /// </summary>
    public static async Task<Result> QueryAsync(IEnumerable<string> servers, int timeoutMs = 3000)
    {
        Exception? last = null;
        foreach (var server in servers)
        {
            try
            {
                return await QueryOneAsync(server, timeoutMs).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                last = ex;
            }
        }
        throw new Exception($"모든 NTP 서버 질의 실패: {last?.Message}", last);
    }

    private static async Task<Result> QueryOneAsync(string server, int timeoutMs)
    {
        var addresses = await Dns.GetHostAddressesAsync(server).ConfigureAwait(false);
        var endpoint = new IPEndPoint(addresses[0], 123);

        var request = new byte[48];
        request[0] = 0x1B; // LI=0, VN=3, Mode=3(client)

        using var udp = new UdpClient(addresses[0].AddressFamily);
        udp.Connect(endpoint);

        // t1: 송신 직전 로컬 UTC + Stopwatch 앵커
        long sendTicks = Stopwatch.GetTimestamp();
        DateTime t1 = DateTime.UtcNow;

        await udp.SendAsync(request, request.Length).ConfigureAwait(false);

        var receiveTask = udp.ReceiveAsync();
        if (await Task.WhenAny(receiveTask, Task.Delay(timeoutMs)).ConfigureAwait(false) != receiveTask)
            throw new TimeoutException($"{server} 응답 시간 초과({timeoutMs}ms)");

        // t4: 수신 직후 로컬 UTC + Stopwatch 앵커
        long recvTicks = Stopwatch.GetTimestamp();
        DateTime t4 = DateTime.UtcNow;

        byte[] data = receiveTask.Result.Buffer;
        if (data.Length < 48) throw new Exception("NTP 응답 길이 부족");

        // 서버 수신(t2)=오프셋 32, 서버 송신(t3)=오프셋 40
        DateTime t2 = ReadTimestamp(data, 32);
        DateTime t3 = ReadTimestamp(data, 40);

        // SNTP: offset = ((t2-t1)+(t3-t4))/2, delay = (t4-t1)-(t3-t2)
        TimeSpan offset = TimeSpan.FromTicks(((t2 - t1).Ticks + (t3 - t4).Ticks) / 2);
        TimeSpan delay = (t4 - t1) - (t3 - t2);

        // 수신 순간(t4)의 참 UTC = 로컬 t4 + offset. 이를 recvTicks 에 앵커링.
        return new Result
        {
            AnchorUtc = t4 + offset,
            AnchorTicks = recvTicks,
            Offset = offset,
            RoundTrip = delay,
            Server = server,
        };
    }

    /// <summary>NTP 64bit 타임스탬프(상위 32 초, 하위 32 분수)를 UTC DateTime 으로.</summary>
    private static DateTime ReadTimestamp(byte[] data, int offset)
    {
        ulong seconds = BigEndianUInt32(data, offset);
        ulong fraction = BigEndianUInt32(data, offset + 4);
        // 분수를 ms 로: fraction/2^32 * 1000
        double ms = (seconds * 1000.0) + (fraction * 1000.0 / 4294967296.0);
        return NtpEpoch.AddMilliseconds(ms);
    }

    private static uint BigEndianUInt32(byte[] data, int offset) =>
        ((uint)data[offset] << 24) | ((uint)data[offset + 1] << 16) |
        ((uint)data[offset + 2] << 8) | data[offset + 3];
}
