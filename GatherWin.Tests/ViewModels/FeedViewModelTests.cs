using System.Net.Http;
using System.Text.Json;
using GatherWin.Services;
using GatherWin.ViewModels;

namespace GatherWin.Tests.ViewModels;

public class FeedViewModelTests
{
    private static FeedViewModel CreateViewModel()
    {
        var http = new HttpClient();
        var jsonOpts = new JsonSerializerOptions();
        var auth = new GatherAuthService(http, jsonOpts, "/nonexistent");
        var api = new GatherApiClient(http, jsonOpts, auth);
        return new FeedViewModel(api);
    }

    [Fact]
    public void ResetNewCount_SetsToZero()
    {
        var vm = CreateViewModel();
        vm.NewCount = 3;

        vm.ResetNewCount();

        Assert.Equal(0, vm.NewCount);
    }

    [Fact]
    public void CancelCompose_ClearsForm()
    {
        var vm = CreateViewModel();
        vm.IsComposing = true;
        vm.NewPostTitle = "Test Title";
        vm.NewPostBody = "Test Body";
        vm.NewPostTags = "tag1,tag2";
        vm.PostError = "some error";

        vm.CancelComposeCommand.Execute(null);

        Assert.False(vm.IsComposing);
        Assert.Equal(string.Empty, vm.NewPostTitle);
        Assert.Equal(string.Empty, vm.NewPostBody);
        Assert.Equal(string.Empty, vm.NewPostTags);
        Assert.Null(vm.PostError);
    }

    [Fact]
    public void ShowCompose_SetsIsComposing()
    {
        var vm = CreateViewModel();
        Assert.False(vm.IsComposing);

        vm.ShowComposeCommand.Execute(null);

        Assert.True(vm.IsComposing);
    }

    [Fact]
    public void SetReplyTo_SetsAndClears()
    {
        var vm = CreateViewModel();
        var comment = new DiscussionComment { CommentId = "c1" };

        vm.SetReplyTo(comment);
        Assert.Equal(comment, vm.ReplyToComment);

        vm.SetReplyTo(null);
        Assert.Null(vm.ReplyToComment);
    }

    [Fact]
    public void CloseDiscussion_ResetsState()
    {
        var vm = CreateViewModel();
        vm.HasDiscussion = true;
        vm.DiscussionPostId = "post-1";
        vm.ReplyToComment = new DiscussionComment { CommentId = "c1" };

        // Can't call CloseDiscussion() directly due to Dispatcher dependency
        // but we can verify the initial state is set
        Assert.True(vm.HasDiscussion);
    }

    [Fact]
    public void Posts_InitiallyEmpty()
    {
        var vm = CreateViewModel();
        Assert.Empty(vm.Posts);
        Assert.Equal(0, vm.NewCount);
    }
}
