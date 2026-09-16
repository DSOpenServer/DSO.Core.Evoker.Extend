using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace DSO.Core.Evoker.Extend.TestApi.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    public class TestController : ControllerBase
    {
        [HttpGet("Test10ExtendTests")]
        public void Test10ExtendTests()
        {
            ExtendTests.RunAll();
        }
    }
}
