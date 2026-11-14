using Microsoft.AspNetCore.Mvc;

namespace HRM.Web.Controllers;

/// <summary>MVC host: serves the compiled Angular single-page app for "/" and every client-side route.</summary>
[ApiExplorerSettings(IgnoreApi = true)] // keeps the page-serving action out of Swagger (it has no explicit HTTP method)
public class HomeController : Controller
{
    private readonly IWebHostEnvironment _env;
    public HomeController(IWebHostEnvironment env) => _env = env;

    public IActionResult Index()
    {
        var index = Path.Combine(_env.WebRootPath ?? Path.Combine(_env.ContentRootPath, "wwwroot"), "index.html");
        if (System.IO.File.Exists(index))
            return PhysicalFile(index, "text/html");

        return Content(
            "<h2>HRM API is running</h2>" +
            "<p>The Angular client is not built yet. From the <code>client</code> folder run <code>npm install</code> and <code>npm run build</code>, " +
            "or run <code>npm start</code> and open http://localhost:4200.</p>" +
            "<p>API docs: <a href=\"/swagger\">/swagger</a></p>",
            "text/html");
    }
}
