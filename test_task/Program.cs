using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using test_task.Data;

var builder = WebApplication.CreateBuilder(args);

// Получаем строку подключения из appsettings.json
var connectionString = builder.Configuration.GetConnectionString("DefaultConnection");

// Регистрируем ApplicationDbContext с провайдером Npgsql (PostgreSQL)
builder.Services.AddDbContext<ApplicationDbContext>(options =>
    options.UseNpgsql(connectionString));

// Добавляем поддержку MVC: контроллеры + представления
builder.Services.AddControllersWithViews();

var app = builder.Build();

// Обработка ошибок и безопасность
if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Home/Error");
    app.UseHsts();
}

// Маршрутизация и статические файлы
app.UseHttpsRedirection();
app.UseStaticFiles();
app.UseRouting();
app.UseAuthorization();

// 1) Меняем маршрут по умолчанию, чтобы корень "/" вызывал ExcelUpload/Upload
app.MapControllerRoute(
    name: "default",
    pattern: "{controller=ExcelUpload}/{action=Upload}/{id?}");

app.Run();
