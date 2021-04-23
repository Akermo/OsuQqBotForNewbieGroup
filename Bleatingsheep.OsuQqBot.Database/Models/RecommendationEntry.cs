﻿using System;
using System.Collections.Generic;
using Bleatingsheep.Osu;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace Bleatingsheep.OsuQqBot.Database.Models
{
    public class RecommendationEntry
    {
        public int Id { get; set; }
        public RecommendationBeatmapId Left { get; set; }
        public RecommendationBeatmapId Recommendation { get; set; }
        public double RecommendationDegree { get; set; }
    }

    [Owned]
    public class RecommendationBeatmapId : IEquatable<RecommendationBeatmapId>
    {
        public static readonly ValueConverter<RecommendationBeatmapId, long> ValueConverter = new(
            v => ((long)v.BeatmapId << 34) | ((long)v.Mode << 32) | (long)v.ValidMods,
            v => new RecommendationBeatmapId
            {
                BeatmapId = (int)(v >> 34),
                Mode = (Mode)((v >> 32) & 0x3),
                ValidMods = (Mods)(v & 0xffffff),
            });

        public int BeatmapId { get; set; }
        public Mode Mode { get; set; }
        public Mods ValidMods { get; set; }

        public override bool Equals(object obj) => Equals(obj as RecommendationBeatmapId);
        public bool Equals(RecommendationBeatmapId other) => other != null && BeatmapId == other.BeatmapId && Mode == other.Mode && ValidMods == other.ValidMods;
        public override int GetHashCode() => HashCode.Combine(BeatmapId, Mode, ValidMods);

        public static bool operator ==(RecommendationBeatmapId left, RecommendationBeatmapId right) => EqualityComparer<RecommendationBeatmapId>.Default.Equals(left, right);
        public static bool operator !=(RecommendationBeatmapId left, RecommendationBeatmapId right) => !(left == right);
    }
}
