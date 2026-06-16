using Clinco.Application.Services;
using Domain.Entities;
using Domain.Interfaces;
using Domain.Interfaces.Repositories;
using MediatR;
using Microsoft.Extensions.Logging;

namespace Application.Features.Notifications.Commands;

// ── Command ───────────────────────────────────────────────────────────────────

public record SendSmsNotificationCommand(
    int AppointmentId,
    int PatientId,
    string PhoneNumber,
    string MessageType,
    string MessageContent) : IRequest;

// ── Handler ───────────────────────────────────────────────────────────────────

public class SendSmsNotificationCommandHandler : IRequestHandler<SendSmsNotificationCommand>
{
    private readonly ISmsNotificationRepository _notifications;
    private readonly ISmsGateway _smsService;
    private readonly IUnitOfWork _uow;
    private readonly ILogger<SendSmsNotificationCommandHandler> _logger;

    public SendSmsNotificationCommandHandler(
        ISmsNotificationRepository notifications,
        ISmsGateway smsService,
        IUnitOfWork uow,
        ILogger<SendSmsNotificationCommandHandler> logger)
    {
        _notifications = notifications;
        _smsService = smsService;
        _uow = uow;
        _logger = logger;
    }

    public async Task Handle(SendSmsNotificationCommand cmd, CancellationToken ct)
    {
        // 1. Persist with Pending status before calling external provider
        var notification = SmsNotification.Create(
            cmd.AppointmentId,
            cmd.PatientId,
            cmd.PhoneNumber,
            cmd.MessageType,
            cmd.MessageContent);

        await _notifications.CreateAsync(notification, ct);
        await _uow.SaveChangesAsync(ct);

        // 2. Call Twilio (or whichever provider ISmsService resolves to)
        var result = await _smsService.SendAsync(cmd.PhoneNumber, cmd.MessageContent, ct);

        // 3. Update status based on provider response
        if (result.IsSuccess)
        {
            notification.MarkSent(result.ProviderMessageId!);
            _logger.LogInformation(
                "SMS sent to {Phone} for appointment {AppointmentId}. SID: {Sid}",
                cmd.PhoneNumber, cmd.AppointmentId, result.ProviderMessageId);
        }
        else
        {
            notification.MarkFailed(result.ErrorMessage ?? "Unknown error");
            _logger.LogWarning(
                "SMS failed for appointment {AppointmentId} to {Phone}. Error: {Error}",
                cmd.AppointmentId, cmd.PhoneNumber, result.ErrorMessage);
        }

        await _notifications.UpdateAsync(notification, ct);
        await _uow.SaveChangesAsync(ct);
    }
}
