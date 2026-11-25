using ShellInterop.Core.JumpLists;

namespace ShellInterop.Core.Tests;

/// <summary>Guards the integer values of public enums as published in 0.2.0-preview.1 (they may be persisted or compared as ints by consumers).</summary>
public class PublicEnumCompatibilityTests
{
    [Theory]
    [InlineData(JumpListApplyStatus.Success, 0)]
    [InlineData(JumpListApplyStatus.ValidationFailed, 1)]
    [InlineData(JumpListApplyStatus.BeginFailed, 2)]
    [InlineData(JumpListApplyStatus.TaskCreationFailed, 3)]
    [InlineData(JumpListApplyStatus.AddTasksFailed, 4)]
    [InlineData(JumpListApplyStatus.CommitFailed, 5)]
    [InlineData(JumpListApplyStatus.AbortFailed, 6)]
    [InlineData(JumpListApplyStatus.DeleteFailed, 7)]
    [InlineData(JumpListApplyStatus.UnknownState, 8)]
    [InlineData(JumpListApplyStatus.Cancelled, 9)]
    [InlineData(JumpListApplyStatus.PublisherDisposed, 10)]
    [InlineData(JumpListApplyStatus.RemovedDestinationsPending, 11)]
    [InlineData(JumpListApplyStatus.RemovedDestinationUnidentifiable, 12)]
    [InlineData(JumpListApplyStatus.CategoryAppendFailed, 13)]
    public void ApplyStatus_PublishedValues_AreUnchanged(JumpListApplyStatus status, int published)
        => Assert.Equal(published, (int)status);

    [Fact]
    public void ApplyStatus_PlanFailed_IsAppended_AndDistinct()
    {
        Assert.Equal(14, (int)JumpListApplyStatus.PlanFailed);
        var values = Enum.GetValues<JumpListApplyStatus>().Select(v => (int)v).ToArray();
        Assert.Equal(values.Length, values.Distinct().Count());
        Assert.Equal(15, values.Length);
    }

    [Theory]
    [InlineData(JumpListFailurePhase.None, 0)]
    [InlineData(JumpListFailurePhase.Unexpected, 13)]
    [InlineData(JumpListFailurePhase.PlanGeneration, 14)]
    public void FailurePhase_PublishedValues_AreUnchanged(JumpListFailurePhase phase, int expected)
        => Assert.Equal(expected, (int)phase);
}
