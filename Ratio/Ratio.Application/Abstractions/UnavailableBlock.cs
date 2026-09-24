namespace Ratio.Application.Abstractions;

public sealed record UnavailableBlock(string Block, UnavailableReason Reason, string Message);
