using NLog.Targets;

namespace ServiceLib.Services;

public class SpeedtestService(Config config, Func<SpeedTestResult, Task> updateFunc)
{
    private static readonly string _tag = "SpeedtestService";
    private readonly Config? _config = config;
    private readonly Func<SpeedTestResult, Task>? _updateFunc = updateFunc;
    private static readonly ConcurrentBag<string> _lstExitLoop = new();

    public void RunLoop(ESpeedActionType actionType, List<ProfileItem> selecteds)
    {
        Task.Run(async () =>
        {
            await RunAsync(actionType, selecteds);
            await ProfileExManager.Instance.SaveTo();
            await UpdateFunc("", ResUI.SpeedtestingCompleted);
        });
    }

    public void ExitLoop()
    {
        if (!_lstExitLoop.IsEmpty)
        {
            _ = UpdateFunc("", ResUI.SpeedtestingStop);

            _lstExitLoop.Clear();
        }
    }

    private static bool ShouldStopTest(string exitLoopKey)
    {
        return !_lstExitLoop.Any(p => p == exitLoopKey);
    }

    private async Task RunAsync(ESpeedActionType actionType, List<ProfileItem> selecteds)
    {
        var exitLoopKey = Utils.GetGuid(false);
        _lstExitLoop.Add(exitLoopKey);

        var lstSelected = await GetClearItem(actionType, selecteds);

        switch (actionType)
        {
            case ESpeedActionType.Tcping:
                await RunTcpingAsync(lstSelected);
                break;

            case ESpeedActionType.Realping:
                await RunRealPingBatchAsync(lstSelected, exitLoopKey);
                break;

            case ESpeedActionType.Speedtest:
                await RunMixedTestAsync(lstSelected, 1, true, exitLoopKey);
                break;

            case ESpeedActionType.Mixedtest:
                await RunMixedTestAsync(lstSelected, _config.SpeedTestItem.MixedConcurrencyCount, true, exitLoopKey);
                break;

            case ESpeedActionType.TestMe:
                await RunTestMeAsync(lstSelected, exitLoopKey);
                break;
        }
    }

    private async Task<List<ServerTestItem>> GetClearItem(ESpeedActionType actionType, List<ProfileItem> selecteds)
    {
        var lstSelected = new List<ServerTestItem>(selecteds.Count);
        var ids = selecteds.Where(it => !it.IndexId.IsNullOrEmpty()
            && it.ConfigType != EConfigType.Custom
            && (it.ConfigType.IsComplexType() || it.Port > 0))
            .Select(it => it.IndexId)
            .ToList();
        var profileMap = await AppManager.Instance.GetProfileItemsByIndexIdsAsMap(ids);
        for (var i = 0; i < selecteds.Count; i++)
        {
            var it = selecteds[i];
            if (it.ConfigType == EConfigType.Custom)
            {
                continue;
            }

            if (!it.ConfigType.IsComplexType() && it.Port <= 0)
            {
                continue;
            }

            var profile = profileMap.GetValueOrDefault(it.IndexId, it);
            lstSelected.Add(new ServerTestItem()
            {
                IndexId = it.IndexId,
                Address = it.Address,
                Port = it.Port,
                ConfigType = it.ConfigType,
                QueueNum = i,
                Profile = profile,
                CoreType = AppManager.Instance.GetCoreType(profile, it.ConfigType),
            });
        }

        //clear test result
        foreach (var it in lstSelected)
        {
            switch (actionType)
            {
                case ESpeedActionType.Tcping:
                case ESpeedActionType.Realping:
                    await UpdateFunc(it.IndexId, ResUI.Speedtesting, "");
                    ProfileExManager.Instance.SetTestDelay(it.IndexId, 0);
                    break;

                case ESpeedActionType.Speedtest:
                    await UpdateFunc(it.IndexId, "", ResUI.SpeedtestingWait);
                    ProfileExManager.Instance.SetTestSpeed(it.IndexId, 0);
                    break;

                case ESpeedActionType.Mixedtest:
                    await UpdateFunc(it.IndexId, ResUI.Speedtesting, ResUI.SpeedtestingWait);
                    ProfileExManager.Instance.SetTestDelay(it.IndexId, 0);
                    ProfileExManager.Instance.SetTestSpeed(it.IndexId, 0);
                    break;

                case ESpeedActionType.TestMe:
                    await UpdateFunc(it.IndexId, "", "", ResUI.SpeedtestingWait); // 初始化状态
                    ProfileExManager.Instance.SetTestAvailability(it.IndexId, "");
                    break;
            }
        }

        if (lstSelected.Count > 1 && (actionType == ESpeedActionType.Speedtest || actionType == ESpeedActionType.Mixedtest))
        {
            NoticeManager.Instance.Enqueue(ResUI.SpeedtestingPressEscToExit);
        }

        return lstSelected;
    }

    private async Task RunTcpingAsync(List<ServerTestItem> selecteds)
    {
        List<Task> tasks = [];
        foreach (var it in selecteds)
        {
            tasks.Add(Task.Run(async () =>
            {
                try
                {
                    var responseTime = await GetTcpingTime(it.Address, it.Port);

                    ProfileExManager.Instance.SetTestDelay(it.IndexId, responseTime);
                    await UpdateFunc(it.IndexId, responseTime.ToString());
                }
                catch (Exception ex)
                {
                    Logging.SaveLog(_tag, ex);
                }
            }));
        }
        await Task.WhenAll(tasks);
    }

    private async Task RunRealPingBatchAsync(List<ServerTestItem> lstSelected, string exitLoopKey, int pageSize = 0)
    {
        if (pageSize <= 0)
        {
            pageSize = lstSelected.Count < Global.SpeedTestPageSize ? lstSelected.Count : Global.SpeedTestPageSize;
        }
        var lstTest = GetTestBatchItem(lstSelected, pageSize);

        List<ServerTestItem> lstFailed = new();
        foreach (var lst in lstTest)
        {
            var ret = await RunRealPingAsync(lst, exitLoopKey);
            if (ret == false)
            {
                lstFailed.AddRange(lst);
            }
            await Task.Delay(100);
        }

        //Retest the failed part
        var pageSizeNext = pageSize / 2;
        if (lstFailed.Count > 0 && pageSizeNext > 0)
        {
            if (ShouldStopTest(exitLoopKey))
            {
                await UpdateFunc("", ResUI.SpeedtestingSkip);
                return;
            }

            await UpdateFunc("", string.Format(ResUI.SpeedtestingTestFailedPart, lstFailed.Count));

            if (pageSizeNext > _config.SpeedTestItem.MixedConcurrencyCount)
            {
                await RunRealPingBatchAsync(lstFailed, exitLoopKey, pageSizeNext);
            }
            else
            {
                await RunMixedTestAsync(lstSelected, _config.SpeedTestItem.MixedConcurrencyCount, false, exitLoopKey);
            }
        }
    }

    private async Task<bool> RunRealPingAsync(List<ServerTestItem> selecteds, string exitLoopKey)
    {
        ProcessService processService = null;
        try
        {
            processService = await CoreManager.Instance.LoadCoreConfigSpeedtest(selecteds);
            if (processService is null)
            {
                return false;
            }
            await Task.Delay(1000);

            List<Task> tasks = new();
            foreach (var it in selecteds)
            {
                if (!it.AllowTest)
                {
                    await UpdateFunc(it.IndexId, ResUI.SpeedtestingSkip);
                    continue;
                }

                if (ShouldStopTest(exitLoopKey))
                {
                    return false;
                }

                tasks.Add(Task.Run(async () =>
                {
                    await DoRealPing(it);
                }));
            }
            await Task.WhenAll(tasks);
        }
        catch (Exception ex)
        {
            Logging.SaveLog(_tag, ex);
        }
        finally
        {
            if (processService != null)
            {
                await processService?.StopAsync();
            }
        }
        return true;
    }

    private async Task RunMixedTestAsync(List<ServerTestItem> selecteds, int concurrencyCount, bool blSpeedTest, string exitLoopKey)
    {
        using var concurrencySemaphore = new SemaphoreSlim(concurrencyCount);
        var downloadHandle = new DownloadService();
        List<Task> tasks = new();
        foreach (var it in selecteds)
        {
            if (ShouldStopTest(exitLoopKey))
            {
                await UpdateFunc(it.IndexId, "", ResUI.SpeedtestingSkip);
                continue;
            }
            await concurrencySemaphore.WaitAsync();

            tasks.Add(Task.Run(async () =>
            {
                ProcessService processService = null;
                try
                {
                    processService = await CoreManager.Instance.LoadCoreConfigSpeedtest(it);
                    if (processService is null)
                    {
                        await UpdateFunc(it.IndexId, "", ResUI.FailedToRunCore);
                        return;
                    }

                    await Task.Delay(1000);

                    var delay = await DoRealPing(it);
                    if (blSpeedTest)
                    {
                        if (ShouldStopTest(exitLoopKey))
                        {
                            await UpdateFunc(it.IndexId, "", ResUI.SpeedtestingSkip);
                            return;
                        }

                        if (delay > 0)
                        {
                            await DoSpeedTest(downloadHandle, it);
                        }
                        else
                        {
                            await UpdateFunc(it.IndexId, "", ResUI.SpeedtestingSkip);
                        }
                    }
                }
                catch (Exception ex)
                {
                    Logging.SaveLog(_tag, ex);
                }
                finally
                {
                    if (processService != null)
                    {
                        await processService?.StopAsync();
                    }
                    concurrencySemaphore.Release();
                }
            }));
        }
        await Task.WhenAll(tasks);
    }

    private async Task RunTestMeAsync(List<ServerTestItem> lstSelected, string exitLoopKey)
    {
        // 1. 获取所有启用的目标
        var enabledTargets = _config.AvailabilityTargets?.Where(x => x.IsEnabled).ToList();
        if (enabledTargets == null || !enabledTargets.Any())
        {
            NoticeManager.Instance.Enqueue("未找到启用的检测目标！");
            return;
        }

        // 2. 按页分批次测试，避免同时启动过多 Core 进程
        int pageSize = lstSelected.Count < Global.SpeedTestPageSize ? lstSelected.Count : Global.SpeedTestPageSize;
        var lstTest = GetTestBatchItem(lstSelected, pageSize);

        foreach (var batch in lstTest)
        {
            if (ShouldStopTest(exitLoopKey))
            {
                return;
            }

            ProcessService processService = null;
            try
            {
                // 3. 为当前批次启动本地代理 Core
                processService = await CoreManager.Instance.LoadCoreConfigSpeedtest(batch);
                if (processService is null)
                {
                    foreach (var it in batch)
                    {
                        ProfileExManager.Instance.SetTestAvailability(it.IndexId, "Core Error");
                        await UpdateFunc(it.IndexId, "", "", "Core Error");
                    }
                    continue;
                }

                // 给 Core 一点启动时间
                await Task.Delay(1000);

                List<Task> tasks = new();
                foreach (var it in batch)
                {
                    if (!it.AllowTest)
                    {
                        continue;
                    }

                    if (ShouldStopTest(exitLoopKey))
                    {
                        return;
                    }

                    tasks.Add(Task.Run(async () =>
                    {
                        var successList = new List<string>();
                        var handler = new SocketsHttpHandler
                        {
                            Proxy = new WebProxy($"socks5://{Global.Loopback}:{it.Port}"),
                            UseProxy = true,
                            PooledConnectionLifetime = TimeSpan.FromSeconds(5)
                        };
                        var ua = enabledTargets.FirstOrDefault(t => !string.IsNullOrEmpty(t.UserAgent))?.UserAgent;
                        using var client = new HttpClient(handler);
                        client.Timeout = TimeSpan.FromSeconds(_config.SpeedTestItem.SpeedTestTimeout > 0 ? _config.SpeedTestItem.SpeedTestTimeout : 5);
                        client.DefaultRequestHeaders.Add("User-Agent", string.IsNullOrEmpty(ua) ? "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/120.0.0.0 Safari/537.36" : ua);
                        client.DefaultRequestHeaders.Add("Accept", "text/html,application/xhtml+xml,application/xml;q=0.9,image/webp,*/*;q=0.8");
                        client.DefaultRequestHeaders.Add("Accept-Language", "en-US,en;q=0.5");
                        foreach (var target in enabledTargets)
                        {
                            try
                            {
                                var response = await client.GetAsync(target.TestUrl);
                                if (response.IsSuccessStatusCode)
                                {
                                    var content = await response.Content.ReadAsStringAsync();
                                    Debug.WriteLine($"---{target.DestinationName}---");
                                    Debug.WriteLine(content);
                                    Debug.WriteLine($"---结束---\r\n");
                                    if (string.IsNullOrEmpty(target.SuccessKeywords) || content.Contains(target.SuccessKeywords))
                                    {
                                        successList.Add(target.DestinationName);
                                    }
                                }
                            }
                            catch { }
                        }
                        string resultStr = successList.Count > 0 ? string.Join(", ", successList) : "Fail";

                        //ProfileExManager.Instance.SetTestAvailability(it.IndexId, resultStr);
                        //await UpdateFunc(it.IndexId, "", "", resultStr);
                        //string resultStr = "Fail";
                        //try
                        //{
                        //    // 4. 配置 HttpClient 并绑定当前节点的本地代理端口
                        //    var handler = new SocketsHttpHandler
                        //    {
                        //        Proxy = new WebProxy($"socks5://{Global.Loopback}:{it.Port}"),
                        //        UseProxy = true,
                        //        PooledConnectionLifetime = TimeSpan.FromSeconds(5)
                        //    };

                        //    using var client = new HttpClient(handler);
                        //    // 使用配置中的超时时间，默认给 10 秒
                        //    client.Timeout = TimeSpan.FromSeconds(_config.SpeedTestItem.SpeedTestTimeout > 0 ? _config.SpeedTestItem.SpeedTestTimeout : 10);

                        //    // 5. 添加请求头，伪装真实浏览器以绕过基础的 WAF 拦截
                        //    client.DefaultRequestHeaders.Add("User-Agent", string.IsNullOrEmpty(target.UserAgent) ? "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/120.0.0.0 Safari/537.36" : target.UserAgent);
                        //    client.DefaultRequestHeaders.Add("Accept", "text/html,application/xhtml+xml,application/xml;q=0.9,image/webp,*/*;q=0.8");
                        //    client.DefaultRequestHeaders.Add("Accept-Language", "en-US,en;q=0.5");

                        //    // 6. 发起真实请求并获取网页内容
                        //    var response = await client.GetAsync(target.TestUrl);

                        //    if (response.IsSuccessStatusCode)
                        //    {
                        //        var content = await response.Content.ReadAsStringAsync();
                        //        Debug.WriteLine(content);
                        //        // 7. 判断关键字匹配
                        //        if (string.IsNullOrEmpty(target.SuccessKeywords))
                        //        {
                        //            resultStr = "OK"; // 如果未配置关键字，状态码 200 即视为可用
                        //        }
                        //        else if (content.Contains(target.SuccessKeywords))
                        //        {
                        //            resultStr = target.DestinationName; // 例如显示 "Gemini"
                        //        }
                        //        else
                        //        {
                        //            resultStr = "Unsupported Region"; // 状态码对，但关键字没匹配上（例如被重定向到了不支持区域页面）
                        //        }
                        //    }
                        //    else
                        //    {
                        //        // 记录 HTTP 错误状态码（如 403 代表可能被 CF 拦截）
                        //        resultStr = $"HTTP {(int)response.StatusCode}";
                        //    }
                        //}
                        //catch (TaskCanceledException)
                        //{
                        //    resultStr = "Timeout";
                        //}
                        //catch (Exception ex)
                        //{
                        //    resultStr = "Error";
                        //    Logging.SaveLog(_tag, ex);
                        //}

                        // 8. 写入底层管理类并刷新 UI (注意这里假设你已经给 UpdateFunc 加了第四个参数处理 Availability)
                        ProfileExManager.Instance.SetTestAvailability(it.IndexId, resultStr);
                        await UpdateFunc(it.IndexId, "", "", resultStr);
                    }));
                }

                await Task.WhenAll(tasks);
            }
            catch (Exception ex)
            {
                Logging.SaveLog(_tag, ex);
            }
            finally
            {
                // 9. 释放临时 Core
                if (processService != null)
                {
                    await processService.StopAsync();
                }
            }
            await Task.Delay(100);
        }
    }

    private async Task<int> DoRealPing(ServerTestItem it)
    {
        var webProxy = new WebProxy($"socks5://{Global.Loopback}:{it.Port}");
        var responseTime = await ConnectionHandler.GetRealPingTime(_config.SpeedTestItem.SpeedPingTestUrl, webProxy, 10);

        ProfileExManager.Instance.SetTestDelay(it.IndexId, responseTime);
        await UpdateFunc(it.IndexId, responseTime.ToString());
        return responseTime;
    }

    private async Task DoSpeedTest(DownloadService downloadHandle, ServerTestItem it)
    {
        await UpdateFunc(it.IndexId, "", ResUI.Speedtesting);

        var webProxy = new WebProxy($"socks5://{Global.Loopback}:{it.Port}");
        var url = _config.SpeedTestItem.SpeedTestUrl;
        var timeout = _config.SpeedTestItem.SpeedTestTimeout;
        await downloadHandle.DownloadDataAsync(url, webProxy, timeout, async (success, msg) =>
        {
            decimal.TryParse(msg, out var dec);
            if (dec > 0)
            {
                ProfileExManager.Instance.SetTestSpeed(it.IndexId, dec);
            }
            await UpdateFunc(it.IndexId, "", msg);
        });
    }

    private async Task<int> GetTcpingTime(string url, int port)
    {
        var responseTime = -1;

        if (!IPAddress.TryParse(url, out var ipAddress))
        {
            var ipHostInfo = await Dns.GetHostEntryAsync(url);
            ipAddress = ipHostInfo.AddressList.First();
        }

        IPEndPoint endPoint = new(ipAddress, port);
        using Socket clientSocket = new(endPoint.AddressFamily, SocketType.Stream, ProtocolType.Tcp);

        var timer = Stopwatch.StartNew();
        try
        {
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            await clientSocket.ConnectAsync(endPoint, cts.Token).ConfigureAwait(false);
            responseTime = (int)timer.ElapsedMilliseconds;
        }
        catch (OperationCanceledException)
        {
        }
        finally
        {
            timer.Stop();
        }
        return responseTime;
    }

    private List<List<ServerTestItem>> GetTestBatchItem(List<ServerTestItem> lstSelected, int pageSize)
    {
        List<List<ServerTestItem>> lstTest = new();
        var lst1 = lstSelected.Where(t => t.CoreType == ECoreType.Xray).ToList();
        var lst2 = lstSelected.Where(t => t.CoreType == ECoreType.sing_box).ToList();

        for (var num = 0; num < (int)Math.Ceiling(lst1.Count * 1.0 / pageSize); num++)
        {
            lstTest.Add(lst1.Skip(num * pageSize).Take(pageSize).ToList());
        }
        for (var num = 0; num < (int)Math.Ceiling(lst2.Count * 1.0 / pageSize); num++)
        {
            lstTest.Add(lst2.Skip(num * pageSize).Take(pageSize).ToList());
        }

        return lstTest;
    }

    private async Task UpdateFunc(string indexId, string delay, string speed = "", string availability = "")
    {
        await _updateFunc?.Invoke(new() { IndexId = indexId, Delay = delay, Speed = speed, Availability = availability });
        if (indexId.IsNotEmpty() && speed.IsNotEmpty())
        {
            ProfileExManager.Instance.SetTestMessage(indexId, speed);
        }
    }
}
