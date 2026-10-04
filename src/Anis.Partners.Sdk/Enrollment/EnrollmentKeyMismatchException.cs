namespace Anis.Partners.Sdk.Enrollment;

/// <summary>The key Anis holds is not the key you submitted. Do not continue this enrollment.</summary>
/// <remarks>
/// <see cref="AnisEnrollmentClient.SubmitKeyAsync"/> computes your key's thumbprint itself and compares it with the
/// one Anis answered with. They differ, so the public key was changed between you and Anis, or Anis answered for a
/// different key. No proof was sent and the answer's challenge must not be used. The invitation already took a key,
/// so ask Anis staff to restart the enrollment, and look at what sits between you and Anis (a proxy that rewrites
/// bodies) before you enrol again.
/// </remarks>
public sealed class EnrollmentKeyMismatchException(string localThumbprint, string? serverThumbprint)
    : Exception("The key Anis holds is not the key you submitted: its thumbprint differs from the one computed locally. "
              + "Do not prove possession and do not continue; ask Anis staff to restart the enrollment.")
{
    /// <summary>The thumbprint of the key you submitted, computed by this SDK.</summary>
    public string LocalThumbprint { get; } = localThumbprint;

    /// <summary>The thumbprint Anis answered with; null when the answer carried none.</summary>
    public string? ServerThumbprint { get; } = serverThumbprint;
}
