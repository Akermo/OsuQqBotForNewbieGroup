﻿using System;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using Bleatingsheep.Osu;
using Bleatingsheep.Osu.ApiClient;
using Bleatingsheep.OsuQqBot.Database.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Polly;

namespace Bleatingsheep.NewHydrant.Data
{
    public class DataProvider : IDataProvider, IDisposable, ILegacyDataProvider
    {
        private readonly IDbContextFactory<NewbieContext> _dbContextFactory;
        private readonly IOsuApiClient _osuApiClient;
        private readonly ILogger<DataProvider> _logger;
        private readonly ThreadLocal<Random> _randomLocal = new(() => new Random());

        public DataProvider(IOsuApiClient osuApiClient, IDbContextFactory<NewbieContext> dbContextFactory, ILogger<DataProvider> logger)
        {
            _osuApiClient = osuApiClient;
            _dbContextFactory = dbContextFactory;
            _logger = logger;
        }

        public async Task<(bool success, BindingInfo? result)> GetBindingInfoAsync(long qq)
        {
            try
            {
                await using var db = _dbContextFactory.CreateDbContext();
                var result = await db.Bindings.SingleOrDefaultAsync(b => b.UserId == qq).ConfigureAwait(false);
                return (true, result);
            }
            catch (Exception e)
            {
                _logger.LogError(e, "{message}", e.Message);
                return (false, default);
            }
        }

        public async Task<(bool success, int? result)> GetBindingIdAsync(long qq)
        {
            var (success, bi) = await GetBindingInfoAsync(qq).ConfigureAwait(false);
            return (success, bi?.OsuId);
        }

        public async Task<int?> GetOsuIdAsync(long qq)
        {
            await using var db = _dbContextFactory.CreateDbContext();
            return (await db.Bindings.AsNoTracking().FirstOrDefaultAsync(b => b.UserId == qq).ConfigureAwait(false))?.OsuId;
        }

        public Task<UserBest[]> GetUserBestRetryAsync(int userId, Mode mode, CancellationToken cancellationToken = default)
        {
            var policy = Policy.Handle<Exception>(e => e is not WebApiClient.HttpStatusFailureException f || f.StatusCode == HttpStatusCode.TooManyRequests)
                .WaitAndRetryForeverAsync(i => TimeSpan.FromMilliseconds((25 << i) + _randomLocal.Value!.Next(50)));
            return policy.ExecuteAsync(_ => _osuApiClient.GetUserBest(userId, mode, 100), cancellationToken);
        }

        public Task<UserInfo> GetUserInfoRetryAsync(int userId, Mode mode, CancellationToken cancellationToken = default)
        {
            var policy = Policy.Handle<Exception>(e => e is not WebApiClient.HttpStatusFailureException f || f.StatusCode == HttpStatusCode.TooManyRequests)
                .WaitAndRetryForeverAsync(i => TimeSpan.FromMilliseconds((i) + _randomLocal.Value!.Next(50)));
            return policy.ExecuteAsync(_ => _osuApiClient.GetUser(userId, mode), cancellationToken);
        }

        public async ValueTask<BeatmapInfo?> GetBeatmapInfoAsync(int beatmapId, Mode mode, CancellationToken cancellationToken = default)
        {
            await using var db = _dbContextFactory.CreateDbContext();
            var cached = await db.BeatmapInfoCache.AsTracking().FirstOrDefaultAsync(c => c.BeatmapId == beatmapId && c.Mode == mode, cancellationToken).ConfigureAwait(false);
            if (cached != null)
            {
                if (cached.BeatmapInfo?.Approved is Approved.Ranked or Approved.Approved
                    || (cached.BeatmapInfo?.Approved == Approved.Loved && cached.CacheDate > DateTimeOffset.UtcNow.AddDays(-183))
                    || cached.CacheDate > DateTimeOffset.UtcNow.AddDays(-14))
                {
                    return cached.BeatmapInfo;
                }
            }
            var currentDate = DateTimeOffset.UtcNow;
            var beatmap = await _osuApiClient.GetBeatmap(beatmapId, mode).ConfigureAwait(false);

            // add to cache
            // null result is also cached
            // if the beatmap is not ranked, info may change.
            TimeSpan? expiresIn = beatmap?.Approved is Approved.Ranked or Approved.Approved
                ? null
                : beatmap?.Approved == Approved.Loved
                ? TimeSpan.FromDays(183)
                : TimeSpan.FromDays(14);
            var expireDate = DateTimeOffset.UtcNow + expiresIn;
            if (cached == null)
            {
                var newEntry = new BeatmapInfoCacheEntry
                {
                    BeatmapId = beatmapId,
                    Mode = mode,
                    CacheDate = currentDate,
                    ExpirationDate = expireDate,
                    BeatmapInfo = beatmap,
                };
                _ = db.BeatmapInfoCache.Add(newEntry);
            }
            else
            {
                cached.CacheDate = currentDate;
                cached.ExpirationDate = expireDate;
                cached.BeatmapInfo = beatmap;
            }
            try
            {
                _ = await db.SaveChangesAsync(CancellationToken.None).ConfigureAwait(false);
            }
            catch
            {
                // ignore this exception
            }

            return beatmap;
        }

        public void Dispose()
        {
            (_randomLocal as IDisposable)?.Dispose();
        }
    }
}
