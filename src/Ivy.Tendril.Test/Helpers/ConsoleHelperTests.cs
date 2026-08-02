using System.Text;
using Ivy.Tendril.Helpers;

namespace Ivy.Tendril.Test.Helpers;

public class ConsoleHelperTests
{
    [Fact]
    public void ReadStream_DecodesUtf8Bytes_NotConsoleCodepage()
    {
        var input = "em dash — CJK 日本";
        var utf8Bytes = Encoding.UTF8.GetBytes(input);

        using var stream = new MemoryStream(utf8Bytes);
        var result = ConsoleHelper.ReadStream(stream);

        Assert.Equal(input, result);
    }

    [Fact]
    public void ReadStream_StripsUtf8Bom()
    {
        var input = "em dash — CJK 日本";
        var utf8Preamble = Encoding.UTF8.GetPreamble();
        var utf8Bytes = Encoding.UTF8.GetBytes(input);
        var bytesWithBom = utf8Preamble.Concat(utf8Bytes).ToArray();

        using var stream = new MemoryStream(bytesWithBom);
        var result = ConsoleHelper.ReadStream(stream);

        Assert.Equal(input, result);
        Assert.DoesNotContain("﻿", result);
    }
}
