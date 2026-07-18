using Ivy.Tendril.Controllers;
using Microsoft.AspNetCore.Mvc;

namespace Ivy.Tendril.Test;

public class PingControllerTests
{
    [Fact]
    public void Get_ReturnsOkWithPong()
    {
        var controller = new PingController();

        var result = controller.Get();

        var okResult = Assert.IsType<OkObjectResult>(result);
        Assert.Equal("pong", okResult.Value);
    }
}
