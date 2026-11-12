namespace AiPlatform.Application.Common.Interfaces;

// এই marker যে command-এ থাকবে সেটাই audit log-এ যায় (opt-in — সব command নয়)।
// AuditAction command নিজে দেয় → entity id/নাম সহ context বসাতে পারে (reflection ছাড়া)।
public interface IAuditableRequest
{
    string AuditAction { get; }
}
