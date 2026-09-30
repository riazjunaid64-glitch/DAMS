using DAMS.Api.Controllers;
using DAMS.Application.DTOs.BookingRequestDtos;
using Microsoft.AspNetCore.Mvc;
using Xunit;

namespace DAMS.Application.Tests;

public sealed class BookingRequestsClosedTests
{
    [Fact]
    public void CreatingABookingRequest_IsRefusedWithAClearMessage()
    {
        var controller = new BookingRequestController(null!);

        var result = controller.CreateBookingRequest(new CreateBookingRequestDto());

        var refused = Assert.IsType<BadRequestObjectResult>(result);
        var message = refused.Value!.GetType().GetProperty("message")!.GetValue(refused.Value);
        Assert.Equal("Booking requests are closed. Please contact our sales team.", message);
    }
}
