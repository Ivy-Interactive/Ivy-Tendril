using Ivy.Tendril.Helpers;

namespace Ivy.Tendril.Test.Helpers;

public class PlanContentHelpersTests
{
    [Fact]
    public void IsFormattingOnly_WhitespaceOnlyDiff_ReturnsTrue()
    {
        var diff = @"diff --git a/test.cs b/test.cs
--- a/test.cs
+++ b/test.cs
@@ -1,5 +1,5 @@
 public class Test
 {
-  public void Method()
-  {
-  }
+    public void Method()
+    {
+    }
";
        var fileDiff = new PlanContentHelpers.FileDiff("test.cs", "M", diff);

        var result = PlanContentHelpers.IsFormattingOnly(fileDiff);

        Assert.True(result);
    }

    [Fact]
    public void IsFormattingOnly_MixedContentAndWhitespace_ReturnsFalse()
    {
        var diff = @"diff --git a/test.cs b/test.cs
--- a/test.cs
+++ b/test.cs
@@ -1,5 +1,5 @@
 public class Test
 {
-    public void Method()
+    public void NewMethod()
     {
     }
";
        var fileDiff = new PlanContentHelpers.FileDiff("test.cs", "M", diff);

        var result = PlanContentHelpers.IsFormattingOnly(fileDiff);

        Assert.False(result);
    }

    [Fact]
    public void IsFormattingOnly_PureContentChange_ReturnsFalse()
    {
        var diff = @"diff --git a/test.cs b/test.cs
--- a/test.cs
+++ b/test.cs
@@ -1,3 +1,3 @@
-var x = 1;
+var x = 2;
";
        var fileDiff = new PlanContentHelpers.FileDiff("test.cs", "M", diff);

        var result = PlanContentHelpers.IsFormattingOnly(fileDiff);

        Assert.False(result);
    }

    [Fact]
    public void IsFormattingOnly_AddedFile_ReturnsFalse()
    {
        var diff = @"diff --git a/test.cs b/test.cs
--- a/test.cs
+++ b/test.cs
@@ -0,0 +1,3 @@
+public class Test
+{
+}
";
        var fileDiff = new PlanContentHelpers.FileDiff("test.cs", "A", diff);

        var result = PlanContentHelpers.IsFormattingOnly(fileDiff);

        Assert.False(result);
    }

    [Fact]
    public void IsFormattingOnly_DeletedFile_ReturnsFalse()
    {
        var diff = @"diff --git a/test.cs b/test.cs
--- a/test.cs
+++ b/test.cs
@@ -1,3 +0,0 @@
-public class Test
-{
-}
";
        var fileDiff = new PlanContentHelpers.FileDiff("test.cs", "D", diff);

        var result = PlanContentHelpers.IsFormattingOnly(fileDiff);

        Assert.False(result);
    }

    [Fact]
    public void IsFormattingOnly_EmptyDiff_ReturnsTrue()
    {
        var fileDiff = new PlanContentHelpers.FileDiff("test.cs", "M", "");

        var result = PlanContentHelpers.IsFormattingOnly(fileDiff);

        Assert.True(result);
    }

    [Fact]
    public void IsFormattingOnly_IndentationChange_ReturnsTrue()
    {
        var diff = @"diff --git a/test.cs b/test.cs
--- a/test.cs
+++ b/test.cs
@@ -1,5 +1,5 @@
 public class Test
 {
-  public void Method()
-  {
-  }
+    public void Method()
+    {
+    }
";
        var fileDiff = new PlanContentHelpers.FileDiff("test.cs", "M", diff);

        var result = PlanContentHelpers.IsFormattingOnly(fileDiff);

        Assert.True(result);
    }

    [Fact]
    public void IsFormattingOnly_ReorderedLines_ReturnsTrue()
    {
        var diff = @"diff --git a/test.cs b/test.cs
--- a/test.cs
+++ b/test.cs
@@ -1,3 +1,3 @@
-var a = 1;
-var b = 2;
+var b = 2;
+var a = 1;
";
        var fileDiff = new PlanContentHelpers.FileDiff("test.cs", "M", diff);

        var result = PlanContentHelpers.IsFormattingOnly(fileDiff);

        Assert.True(result);
    }

    [Fact]
    public void IsFormattingOnly_LineEndingChanges_ReturnsTrue()
    {
        var diff = "diff --git a/test.cs b/test.cs\n" +
                   "--- a/test.cs\n" +
                   "+++ b/test.cs\n" +
                   "@@ -1,2 +1,2 @@\n" +
                   "-var x = 1;\r\n" +
                   "+var x = 1;\n";
        var fileDiff = new PlanContentHelpers.FileDiff("test.cs", "M", diff);

        var result = PlanContentHelpers.IsFormattingOnly(fileDiff);

        Assert.True(result);
    }

    [Fact]
    public void IsFormattingOnly_TrailingWhitespace_ReturnsTrue()
    {
        var diff = @"diff --git a/test.cs b/test.cs
--- a/test.cs
+++ b/test.cs
@@ -1,3 +1,3 @@
-var x = 1;
+var x = 1;
";
        var fileDiff = new PlanContentHelpers.FileDiff("test.cs", "M", diff);

        var result = PlanContentHelpers.IsFormattingOnly(fileDiff);

        Assert.True(result);
    }

    [Fact]
    public void IsFormattingOnly_MultipleHunks_AllFormatting_ReturnsTrue()
    {
        var diff = @"diff --git a/test.cs b/test.cs
--- a/test.cs
+++ b/test.cs
@@ -1,3 +1,3 @@
 public class Test
 {
-  void Method1()
+    void Method1()
@@ -10,3 +10,3 @@
 public class Other
 {
-  void Method2()
+    void Method2()
";
        var fileDiff = new PlanContentHelpers.FileDiff("test.cs", "M", diff);

        var result = PlanContentHelpers.IsFormattingOnly(fileDiff);

        Assert.True(result);
    }

    [Fact]
    public void IsFormattingOnly_MultipleHunks_OneWithContent_ReturnsFalse()
    {
        var diff = @"diff --git a/test.cs b/test.cs
--- a/test.cs
+++ b/test.cs
@@ -1,3 +1,3 @@
 public class Test
 {
-  void Method1()
+    void Method1()
@@ -10,3 +10,3 @@
 public class Other
 {
-    void Method2()
+    void NewMethod()
";
        var fileDiff = new PlanContentHelpers.FileDiff("test.cs", "M", diff);

        var result = PlanContentHelpers.IsFormattingOnly(fileDiff);

        Assert.False(result);
    }

    [Fact]
    public void IsFormattingOnly_OnlyContextLines_ReturnsTrue()
    {
        var diff = @"diff --git a/test.cs b/test.cs
--- a/test.cs
+++ b/test.cs
@@ -1,3 +1,3 @@
 public class Test
 {
 }
";
        var fileDiff = new PlanContentHelpers.FileDiff("test.cs", "M", diff);

        var result = PlanContentHelpers.IsFormattingOnly(fileDiff);

        Assert.True(result);
    }

    [Fact]
    public void IsFormattingOnly_TabsToSpaces_ReturnsTrue()
    {
        var diff = "diff --git a/test.cs b/test.cs\n" +
                   "--- a/test.cs\n" +
                   "+++ b/test.cs\n" +
                   "@@ -1,3 +1,3 @@\n" +
                   " public class Test\n" +
                   " {\n" +
                   "-\tvoid Method()\n" +
                   "+    void Method()\n";
        var fileDiff = new PlanContentHelpers.FileDiff("test.cs", "M", diff);

        var result = PlanContentHelpers.IsFormattingOnly(fileDiff);

        Assert.True(result);
    }

    [Fact]
    public void IsFormattingOnly_BlankLinesAdded_ReturnsFalse()
    {
        var diff = @"diff --git a/test.cs b/test.cs
--- a/test.cs
+++ b/test.cs
@@ -1,3 +1,4 @@
 public class Test
 {
+
 }
";
        var fileDiff = new PlanContentHelpers.FileDiff("test.cs", "M", diff);

        var result = PlanContentHelpers.IsFormattingOnly(fileDiff);

        Assert.False(result);
    }

    [Fact]
    public void IsFormattingOnly_BlankLinesRemoved_ReturnsFalse()
    {
        var diff = @"diff --git a/test.cs b/test.cs
--- a/test.cs
+++ b/test.cs
@@ -1,4 +1,3 @@
 public class Test
 {
-
 }
";
        var fileDiff = new PlanContentHelpers.FileDiff("test.cs", "M", diff);

        var result = PlanContentHelpers.IsFormattingOnly(fileDiff);

        Assert.False(result);
    }

    [Fact]
    public void IsFormattingOnly_CommentSpacingOnly_ReturnsTrue()
    {
        var diff = @"diff --git a/test.cs b/test.cs
--- a/test.cs
+++ b/test.cs
@@ -1,3 +1,3 @@
 public class Test
 {
-  // This is a comment
+    // This is a comment
";
        var fileDiff = new PlanContentHelpers.FileDiff("test.cs", "M", diff);

        var result = PlanContentHelpers.IsFormattingOnly(fileDiff);

        Assert.True(result);
    }

    [Fact]
    public void IsFormattingOnly_CommentContentChange_ReturnsFalse()
    {
        var diff = @"diff --git a/test.cs b/test.cs
--- a/test.cs
+++ b/test.cs
@@ -1,3 +1,3 @@
-// This is a comment
+// This is a different comment
";
        var fileDiff = new PlanContentHelpers.FileDiff("test.cs", "M", diff);

        var result = PlanContentHelpers.IsFormattingOnly(fileDiff);

        Assert.False(result);
    }

    [Fact]
    public void IsFormattingOnly_MissingClosingBrace_ReturnsTrue()
    {
        var diff = @"diff --git a/test.cs b/test.cs
--- a/test.cs
+++ b/test.cs
@@ -1,5 +1,5 @@
 public class Test
 {
     void Method()
     {
-    }
+    }
";
        var fileDiff = new PlanContentHelpers.FileDiff("test.cs", "M", diff);

        var result = PlanContentHelpers.IsFormattingOnly(fileDiff);

        Assert.True(result);
    }

    [Fact]
    public void IsFormattingOnly_LongLineWrapping_ReturnsFalse()
    {
        // Line wrapping is a structural change (1 line becomes 2 lines), not just formatting
        var diff = @"diff --git a/test.cs b/test.cs
--- a/test.cs
+++ b/test.cs
@@ -1,3 +1,3 @@
-var result = SomeVeryLongMethodName(parameter1, parameter2, parameter3);
+var result = SomeVeryLongMethodName(
+    parameter1, parameter2, parameter3);
";
        var fileDiff = new PlanContentHelpers.FileDiff("test.cs", "M", diff);

        var result = PlanContentHelpers.IsFormattingOnly(fileDiff);

        Assert.False(result);
    }

    [Fact]
    public void IsFormattingOnly_StringFormatChange_ReturnsFalse()
    {
        var diff = @"diff --git a/test.cs b/test.cs
--- a/test.cs
+++ b/test.cs
@@ -1,3 +1,3 @@
-var msg = ""Hello World"";
+var msg = ""Hello Universe"";
";
        var fileDiff = new PlanContentHelpers.FileDiff("test.cs", "M", diff);

        var result = PlanContentHelpers.IsFormattingOnly(fileDiff);

        Assert.False(result);
    }

    [Fact]
    public void IsFormattingOnly_ComplexRealWorldIndentation_ReturnsTrue()
    {
        var diff = @"diff --git a/test.cs b/test.cs
--- a/test.cs
+++ b/test.cs
@@ -1,10 +1,10 @@
 public class Test
 {
-  public override object Build()
-  {
-      var client = UseService<IClientProvider>();
-      var hideFormatting = UseState(true);
-      return Layout.Vertical();
-  }
+    public override object Build()
+    {
+        var client = UseService<IClientProvider>();
+        var hideFormatting = UseState(true);
+        return Layout.Vertical();
+    }
 }
";
        var fileDiff = new PlanContentHelpers.FileDiff("test.cs", "M", diff);

        var result = PlanContentHelpers.IsFormattingOnly(fileDiff);

        Assert.True(result);
    }

    [Fact]
    public void IsFormattingOnly_UnequalLineCount_DifferentOrder_ReturnsTrue()
    {
        var diff = @"diff --git a/test.cs b/test.cs
--- a/test.cs
+++ b/test.cs
@@ -1,5 +1,5 @@
-var a = 1;
-var b = 2;
-var c = 3;
+var c = 3;
+var a = 1;
+var b = 2;
";
        var fileDiff = new PlanContentHelpers.FileDiff("test.cs", "M", diff);

        var result = PlanContentHelpers.IsFormattingOnly(fileDiff);

        Assert.True(result);
    }

    [Fact]
    public void IsFormattingOnly_DifferentLineCount_ReturnsFalse()
    {
        var diff = @"diff --git a/test.cs b/test.cs
--- a/test.cs
+++ b/test.cs
@@ -1,3 +1,4 @@
 var a = 1;
 var b = 2;
+var c = 3;
";
        var fileDiff = new PlanContentHelpers.FileDiff("test.cs", "M", diff);

        var result = PlanContentHelpers.IsFormattingOnly(fileDiff);

        Assert.False(result);
    }

    [Fact]
    public void CountDiffLines_SingleFile_CountsAdditionsAndDeletions()
    {
        var diff = @"diff --git a/test.cs b/test.cs
--- a/test.cs
+++ b/test.cs
@@ -1,5 +1,6 @@
 public class Test
 {
-    public void OldMethod()
+    public void NewMethod()
     {
+        var x = 1;
     }
";
        var result = PlanContentHelpers.CountDiffLines(diff);

        Assert.Equal(2, result.Additions);
        Assert.Equal(1, result.Deletions);
    }

    [Fact]
    public void CountDiffLines_SkipsFileHeaders()
    {
        var diff = @"diff --git a/test.cs b/test.cs
index 1234567..abcdefg 100644
--- a/test.cs
+++ b/test.cs
@@ -1,3 +1,3 @@
-var x = 1;
+var x = 2;
";
        var result = PlanContentHelpers.CountDiffLines(diff);

        Assert.Equal(1, result.Additions);
        Assert.Equal(1, result.Deletions);
    }

    [Fact]
    public void CountDiffLines_DeletedCommentLineStartingWithDashes_IsCounted()
    {
        var diff = @"diff --git a/test.sql b/test.sql
--- a/test.sql
+++ b/test.sql
@@ -1,3 +1,2 @@
 SELECT * FROM users
--- old note
 WHERE active = 1;
";
        var result = PlanContentHelpers.CountDiffLines(diff);

        Assert.Equal(0, result.Additions);
        Assert.Equal(1, result.Deletions);
    }

    [Fact]
    public void CountDiffLines_AddedLineStartingWithPluses_IsCounted()
    {
        var diff = @"diff --git a/test.cpp b/test.cpp
--- a/test.cpp
+++ b/test.cpp
@@ -1,2 +1,3 @@
 int x = 5;
+// +++ increment operator
 x++;
";
        var result = PlanContentHelpers.CountDiffLines(diff);

        Assert.Equal(1, result.Additions);
        Assert.Equal(0, result.Deletions);
    }

    [Fact]
    public void CountDiffLines_MultipleFiles_SumsAcrossFiles()
    {
        var diff = @"diff --git a/file1.cs b/file1.cs
--- a/file1.cs
+++ b/file1.cs
@@ -1,2 +1,3 @@
 var a = 1;
+var b = 2;
diff --git a/file2.cs b/file2.cs
--- a/file2.cs
+++ b/file2.cs
@@ -1,3 +1,2 @@
 var x = 10;
-var y = 20;
";
        var result = PlanContentHelpers.CountDiffLines(diff);

        Assert.Equal(1, result.Additions);
        Assert.Equal(1, result.Deletions);
    }

    [Fact]
    public void CountDiffLines_FileDiffList_SumsEachEntry()
    {
        var fileDiffs = new List<PlanContentHelpers.FileDiff>
        {
            new("file1.cs", "M", @"diff --git a/file1.cs b/file1.cs
@@ -1,1 +1,2 @@
 var a = 1;
+var b = 2;
"),
            new("file2.cs", "M", @"diff --git a/file2.cs b/file2.cs
@@ -1,2 +1,1 @@
-var x = 10;
 var y = 20;
")
        };

        var result = PlanContentHelpers.CountDiffLines(fileDiffs);

        Assert.Equal(1, result.Additions);
        Assert.Equal(1, result.Deletions);
    }

    [Fact]
    public void CountDiffLines_NullOrEmptyDiff_ReturnsEmpty()
    {
        var resultNull = PlanContentHelpers.CountDiffLines((string?)null);
        var resultEmpty = PlanContentHelpers.CountDiffLines("");
        var resultWhitespace = PlanContentHelpers.CountDiffLines("   ");

        Assert.Equal(PlanContentHelpers.DiffLineCounts.Empty, resultNull);
        Assert.Equal(PlanContentHelpers.DiffLineCounts.Empty, resultEmpty);
        Assert.Equal(PlanContentHelpers.DiffLineCounts.Empty, resultWhitespace);
    }

    [Fact]
    public void CountDiffLines_BinaryFileDiff_ReturnsEmpty()
    {
        var diff = @"diff --git a/image.png b/image.png
Binary files a/image.png and b/image.png differ
";
        var result = PlanContentHelpers.CountDiffLines(diff);

        Assert.Equal(0, result.Additions);
        Assert.Equal(0, result.Deletions);
    }

    [Fact]
    public void CountDiffLines_CrlfLineEndings_CountsSameAsLf()
    {
        var diffLf = "diff --git a/test.cs b/test.cs\n@@ -1,2 +1,3 @@\n var a = 1;\n+var b = 2;\n-var c = 3;\n";
        var diffCrlf = "diff --git a/test.cs b/test.cs\r\n@@ -1,2 +1,3 @@\r\n var a = 1;\r\n+var b = 2;\r\n-var c = 3;\r\n";

        var resultLf = PlanContentHelpers.CountDiffLines(diffLf);
        var resultCrlf = PlanContentHelpers.CountDiffLines(diffCrlf);

        Assert.Equal(resultLf.Additions, resultCrlf.Additions);
        Assert.Equal(resultLf.Deletions, resultCrlf.Deletions);
        Assert.Equal(1, resultLf.Additions);
        Assert.Equal(1, resultLf.Deletions);
    }

    [Fact]
    public void CountDiffLines_NoNewlineMarker_IsNotCounted()
    {
        var diff = @"diff --git a/test.cs b/test.cs
--- a/test.cs
+++ b/test.cs
@@ -1,2 +1,2 @@
-var x = 1;
+var x = 2;
\ No newline at end of file
";
        var result = PlanContentHelpers.CountDiffLines(diff);

        Assert.Equal(1, result.Additions);
        Assert.Equal(1, result.Deletions);
    }
}
