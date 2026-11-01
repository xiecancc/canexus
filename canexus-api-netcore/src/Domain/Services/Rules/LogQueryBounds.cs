namespace Domain.Services.Rules;

/// <summary>日志查询时间边界纯逻辑（操作日志 / 登录日志共用）。</summary>
public static class LogQueryBounds
{
    /// <summary>
    /// 归一化结束时间：日期零点（yyyy-MM-dd）表示“含当日”，统一外推到次日零点后按「小于」比较。
    /// </summary>
    /// <param name="endTime">原始结束时间。</param>
    /// <returns>归一化后的结束时间；入参为空时返回 null。</returns>
    public static DateTime? NormalizeEndTime(DateTime? endTime)
    {
        if (endTime is null)
        {
            return null;
        }

        return endTime.Value.TimeOfDay == TimeSpan.Zero
            ? endTime.Value.AddDays(1)
            : endTime.Value;
    }
}
