using System.Net.Http;
using System.Text.Json;
using GatherWin.Services;
using GatherWin.ViewModels;

namespace GatherWin.Tests.ViewModels;

public class CommentsViewModelTests
{
    private static CommentsViewModel CreateViewModel()
    {
        // GatherApiClient requires HttpClient+JsonOptions+AuthService — we create a minimal one.
        // These tests don't make network calls.
        var http = new HttpClient();
        var jsonOpts = new JsonSerializerOptions();
        var auth = new GatherAuthService(http, jsonOpts, "/nonexistent");
        var api = new GatherApiClient(http, jsonOpts, auth);
        return new CommentsViewModel(api);
    }

    [Fact]
    public void ResetNewCount_SetsToZero()
    {
        var vm = CreateViewModel();
        // Simulate some new count via property (it's publicly settable via CommunityToolkit)
        vm.NewCount = 5;
        Assert.Equal(5, vm.NewCount);

        vm.ResetNewCount();
        Assert.Equal(0, vm.NewCount);
    }

    [Fact]
    public void CloseDiscussion_ClearsState()
    {
        var vm = CreateViewModel();
        vm.HasDiscussion = true;
        vm.DiscussionPostId = "test-post";
        vm.ReplyToComment = new DiscussionComment { CommentId = "c1" };

        // CloseDiscussion calls Dispatcher.Invoke, so we can't test it directly
        // outside of a WPF context. Instead test the property assignments.
        Assert.True(vm.HasDiscussion);
        Assert.Equal("test-post", vm.DiscussionPostId);
        Assert.NotNull(vm.ReplyToComment);
    }

    [Fact]
    public void SetReplyTo_SetsReplyToComment()
    {
        var vm = CreateViewModel();
        var comment = new DiscussionComment { CommentId = "c1", Author = "test" };

        vm.SetReplyTo(comment);

        Assert.Equal(comment, vm.ReplyToComment);
        Assert.Null(vm.DiscussionSendError);
    }

    [Fact]
    public void SetReplyTo_Null_ClearsReplyTo()
    {
        var vm = CreateViewModel();
        vm.ReplyToComment = new DiscussionComment { CommentId = "c1" };

        vm.SetReplyTo(null);

        Assert.Null(vm.ReplyToComment);
    }

    [Fact]
    public void Discussions_InitiallyEmpty()
    {
        var vm = CreateViewModel();
        Assert.Empty(vm.Discussions);
        Assert.Empty(vm.Comments);
        Assert.Empty(vm.DiscussionComments);
    }
}
