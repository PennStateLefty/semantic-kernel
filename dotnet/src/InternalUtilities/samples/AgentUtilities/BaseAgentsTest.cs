﻿// Copyright (c) Microsoft. All rights reserved.

using System.ClientModel;
using System.Collections.ObjectModel;
using System.Diagnostics;
using Azure.Identity;
using Microsoft.SemanticKernel;
using Microsoft.SemanticKernel.Agents.OpenAI;
using Microsoft.SemanticKernel.ChatCompletion;
using OpenAI.Assistants;
using OpenAI.Files;

using ChatTokenUsage = OpenAI.Chat.ChatTokenUsage;

/// <summary>
/// Base class for samples that demonstrate the usage of agents.
/// </summary>
public abstract class BaseAgentsTest(ITestOutputHelper output) : BaseTest(output, redirectSystemConsoleOutput: true)
{
    /// <summary>
    /// Metadata key to indicate the assistant as created for a sample.
    /// </summary>
    protected const string AssistantSampleMetadataKey = "sksample";

    /// <summary>
    /// Metadata to indicate the assistant as created for a sample.
    /// </summary>
    /// <remarks>
    /// While the samples do attempt delete the assistants it creates, it is possible
    /// that some assistants may remain.  This metadata can be used to identify and sample
    /// agents for clean-up.
    /// </remarks>
    protected static readonly ReadOnlyDictionary<string, string> AssistantSampleMetadata =
        new(new Dictionary<string, string>
        {
            { AssistantSampleMetadataKey, bool.TrueString }
        });

    /// <summary>
    /// Provide a <see cref="OpenAIClientProvider"/> according to the configuration settings.
    /// </summary>
    protected OpenAIClientProvider GetClientProvider()
        =>
            this.UseOpenAIConfig ?
                OpenAIClientProvider.ForOpenAI(new ApiKeyCredential(this.ApiKey ?? throw new ConfigurationNotFoundException("OpenAI:ApiKey"))) :
                !string.IsNullOrWhiteSpace(this.ApiKey) ?
                    OpenAIClientProvider.ForAzureOpenAI(new ApiKeyCredential(this.ApiKey), new Uri(this.Endpoint!)) :
                    OpenAIClientProvider.ForAzureOpenAI(new AzureCliCredential(), new Uri(this.Endpoint!));

    /// <summary>
    /// Common method to write formatted agent chat content to the console.
    /// </summary>
    protected void WriteAgentChatMessage(ChatMessageContent message)
    {
        // Include ChatMessageContent.AuthorName in output, if present.
        string authorExpression = message.Role == AuthorRole.User ? string.Empty : $" - {message.AuthorName ?? "*"}";
        // Include TextContent (via ChatMessageContent.Content), if present.
        string contentExpression = string.IsNullOrWhiteSpace(message.Content) ? string.Empty : message.Content;
        bool isCode = message.Metadata?.ContainsKey(OpenAIAssistantAgent.CodeInterpreterMetadataKey) ?? false;
        string codeMarker = isCode ? "\n  [CODE]\n" : " ";
        Console.WriteLine($"\n# {message.Role}{authorExpression}:{codeMarker}{contentExpression}");

        // Provide visibility for inner content (that isn't TextContent).
        foreach (KernelContent item in message.Items)
        {
            if (item is AnnotationContent annotation)
            {
                Console.WriteLine($"  [{item.GetType().Name}] {annotation.Quote}: File #{annotation.FileId}");
            }
            else if (item is FileReferenceContent fileReference)
            {
                Console.WriteLine($"  [{item.GetType().Name}] File #{fileReference.FileId}");
            }
            else if (item is ImageContent image)
            {
                Console.WriteLine($"  [{item.GetType().Name}] {image.Uri?.ToString() ?? image.DataUri ?? $"{image.Data?.Length} bytes"}");
            }
            else if (item is FunctionCallContent functionCall)
            {
                Console.WriteLine($"  [{item.GetType().Name}] {functionCall.Id}");
            }
            else if (item is FunctionResultContent functionResult)
            {
                Console.WriteLine($"  [{item.GetType().Name}] {functionResult.CallId} - {functionResult.Result?.AsJson() ?? "*"}");
            }
        }

        if (message.Metadata?.TryGetValue("Usage", out object? usage) ?? false)
        {
            if (usage is RunStepTokenUsage assistantUsage)
            {
                WriteUsage(assistantUsage.TotalTokenCount, assistantUsage.InputTokenCount, assistantUsage.OutputTokenCount);
            }
            else if (usage is ChatTokenUsage chatUsage)
            {
                WriteUsage(chatUsage.TotalTokenCount, chatUsage.InputTokenCount, chatUsage.OutputTokenCount);
            }
        }

        void WriteUsage(int totalTokens, int inputTokens, int outputTokens)
        {
            Console.WriteLine($"  [Usage] Tokens: {totalTokens}, Input: {inputTokens}, Output: {outputTokens}");
        }
    }

    protected async Task DownloadResponseContentAsync(OpenAIFileClient client, ChatMessageContent message)
    {
        foreach (KernelContent item in message.Items)
        {
            if (item is AnnotationContent annotation)
            {
                await this.DownloadFileContentAsync(client, annotation.FileId!);
            }
        }
    }

    protected async Task DownloadResponseImageAsync(OpenAIFileClient client, ChatMessageContent message)
    {
        foreach (KernelContent item in message.Items)
        {
            if (item is FileReferenceContent fileReference)
            {
                await this.DownloadFileContentAsync(client, fileReference.FileId, launchViewer: true);
            }
        }
    }

    private async Task DownloadFileContentAsync(OpenAIFileClient client, string fileId, bool launchViewer = false)
    {
        OpenAIFile fileInfo = client.GetFile(fileId);
        if (fileInfo.Purpose == FilePurpose.AssistantsOutput)
        {
            string filePath = Path.Combine(Path.GetTempPath(), Path.GetFileName(fileInfo.Filename));
            if (launchViewer)
            {
                filePath = Path.ChangeExtension(filePath, ".png");
            }

            BinaryData content = await client.DownloadFileAsync(fileId);
            File.WriteAllBytes(filePath, content.ToArray());
            Console.WriteLine($"  File #{fileId} saved to: {filePath}");

            if (launchViewer)
            {
                Process.Start(
                    new ProcessStartInfo
                    {
                        FileName = "cmd.exe",
                        Arguments = $"/C start {filePath}"
                    });
            }
        }
    }
}