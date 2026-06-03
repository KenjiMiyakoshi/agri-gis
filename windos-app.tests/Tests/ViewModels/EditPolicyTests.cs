using AgriGis.Desktop.ViewModels;
using Xunit;

namespace AgriGis.Desktop.Tests.Tests.ViewModels;

// H5-204 (WH5-2): EditPolicy.ShouldBeReadOnly の純関数テスト。
// 4 ケース (guest × asOf) を網羅。asOf 解除時の guest 復元バグ予防の根拠。
public sealed class EditPolicyTests
{
    [Theory]
    [InlineData(false, false, false)]  // 通常: 編集可
    [InlineData(false, true,  true)]   // asOf 過去時点: 編集不可
    [InlineData(true,  false, true)]   // guest: 編集不可
    [InlineData(true,  true,  true)]   // guest + asOf: 編集不可
    public void ShouldBeReadOnly_AllCombinations(bool isGuest, bool isAsOfActive, bool expected)
    {
        Assert.Equal(expected, EditPolicy.ShouldBeReadOnly(isGuest, isAsOfActive));
    }
}
