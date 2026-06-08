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
    public void IsFormattingOnly_CommentFormattingChange_ReturnsTrue()
    {
        var diff = @"diff --git a/test.cs b/test.cs
--- a/test.cs
+++ b/test.cs
@@ -1,3 +1,3 @@
 // This is a comment
-//with bad formatting
+// with proper formatting
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
    public void IsFormattingOnly_LongLineWrapping_ReturnsTrue()
    {
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

        Assert.True(result);
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
}
