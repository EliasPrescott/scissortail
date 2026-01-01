namespace Scissortail;

public class WhoIsResult {
    public required WhoIsNodeDetails Node { get; set; }
    public required WhoIsUserProfile UserProfile { get; set; }
    public required WhoIsCapMap CapMap { get; set; }
}

public class WhoIsNodeDetails {
    public required long ID { get; set; }
    public required string StableID { get; set; }
    public required string Name { get; set; }
    public required long User { get; set; }
    public required string Key { get; set; }
    public required string KeyExpiry { get; set; }
    public required string Machine { get; set; }
    public required string DiscoKey { get; set; }
    public required List<string> Addresses { get; set; }
    public required List<string> AllowedIPs { get; set; }
    public required int HomeDERP { get; set; }
    public required WhoIsHostInfo HostInfo { get; set; }
    public required string Created { get; set; }
    public required int Cap { get; set; }
    public required bool Online { get; set; }
    public required string ComputedName { get; set; }
    public required string ComputedNameWithHost { get; set; }
}

public class WhoIsHostInfo {
    public required string OS { get; set; }
    public required string Hostname { get; set; }
}

public class WhoIsUserProfile {
    public required long ID { get; set; }
    public required string LoginName { get; set; }
    public required string DisplayName { get; set; }
    public required string ProfilePicURL { get; set; }
}

public class WhoIsCapMap {
}
