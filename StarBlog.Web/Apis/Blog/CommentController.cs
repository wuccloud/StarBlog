﻿using System.Text.Json;
using CodeLab.Share.Contrib.StopWords;
using CodeLab.Share.ViewModels.Response;
using Microsoft.AspNetCore.Mvc;
using StarBlog.Data.Models;
using StarBlog.Web.Extensions;
using StarBlog.Web.Services;
using StarBlog.Web.ViewModels.Comments;
using StarBlog.Web.ViewModels.QueryFilters;

namespace StarBlog.Web.Apis.Blog;

[Route("Api/[controller]")]
[ApiController]
public class CommentController : ControllerBase {
    private readonly CommentService _commentService;
    private readonly TempFilterService _filter;

    public CommentController(CommentService commentService, TempFilterService filter) {
        _commentService = commentService;
        _filter = filter;
    }

    /// <summary>
    /// 获取分页评论
    /// </summary>
    [HttpGet]
    public async Task<ApiResponsePaged<Comment>> GetPagedList([FromQuery] CommentQueryParameters @params) {
        var (data, meta) = await _commentService.GetPagedList(@params);
        return new ApiResponsePaged<Comment>(data, meta);
    }

    /// <summary>
    /// 根据邮箱和验证码，获取匿名用户信息
    /// </summary>
    [HttpGet("[action]")]
    public async Task<ApiResponse> GetAnonymousUser(string email, string otp) {
        if (!CommentService.IsValidEmail(email)) return ApiResponse.BadRequest("提供的邮箱地址无效");

        var verified = _commentService.VerifyOtp(email, otp);
        if (!verified) return ApiResponse.BadRequest("验证码无效");

        var anonymous = await _commentService.GetAnonymousUser(email);
        var (_, newOtp) = await _commentService.GenerateOtp(email, true);

        return ApiResponse.Ok(new {
            AnonymousUser = anonymous,
            NewOtp = newOtp
        });
    }

    /// <summary>
    /// 获取邮件验证码
    /// </summary>
    [HttpGet("[action]")]
    public async Task<ApiResponse> GetEmailOtp(string email) {
        if (!CommentService.IsValidEmail(email)) {
            return ApiResponse.BadRequest("提供的邮箱地址无效");
        }

        var (result, _) = await _commentService.GenerateOtp(email);
        return result
            ? ApiResponse.Ok("发送邮件验证码成功，五分钟内有效")
            : ApiResponse.BadRequest("上一个验证码还在有效期内，请勿重复请求验证码");
    }

    [HttpPost]
    public async Task<ApiResponse<Comment>> Add(CommentCreationDto dto) {
        if (!_commentService.VerifyOtp(dto.Email, dto.EmailOtp)) {
            return ApiResponse.BadRequest("验证码无效");
        }

        var anonymousUser = await _commentService.GetOrCreateAnonymousUser(
            dto.UserName, dto.Email, dto.Url,
            HttpContext.GetRemoteIPAddress()?.ToString().Split(":")?.Last()
        );

        var comment = new Comment {
            ParentId = dto.ParentId,
            PostId = dto.PostId,
            AnonymousUserId = anonymousUser.Id,
            UserAgent = Request.Headers.UserAgent,
            Content = dto.Content
        };

        string msg;
        if (_filter.CheckBadWord(dto.Content)) {
            comment.Visible = false;
            msg = "小管家发现您可能使用了不良用语，该评论将在审核通过后展示~";
        }
        else {
            comment.Visible = true;
            msg = "评论由小管家审核通过，感谢您参与讨论~";
        }

        comment = await _commentService.Add(comment);

        return new ApiResponse<Comment>(comment) {
            Message = msg
        };
    }

    [HttpGet("[action]")]
    public async Task<ApiResponse> CheckBadWord(string word) {
        return ApiResponse.Ok(_filter.CheckBadWord(word).ToString());
    }
}