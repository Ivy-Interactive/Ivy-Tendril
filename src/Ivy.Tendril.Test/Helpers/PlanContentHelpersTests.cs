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
}
