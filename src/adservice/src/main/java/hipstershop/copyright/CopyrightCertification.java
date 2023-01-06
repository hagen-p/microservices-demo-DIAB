package hipstershop.copyright;

import hipstershop.AdService;
import java.util.HashMap;
import java.util.List;
import java.util.Map;
import org.apache.logging.log4j.LogManager;
import org.apache.logging.log4j.Logger;

public class CopyrightCertification {
    private static final Logger logger = LogManager.getLogger(AdService.class);

    private final Map<String, Certifier> certifiers = new HashMap<>();
    private final boolean enabled = Boolean.parseBoolean(System.getenv().getOrDefault("ENABLE_COPYRIGHT_CERTIFICATION", "false"));

    {
        certifiers.put("vintage", new BasicCertifier());
        certifiers.put("cycling", new BasicCertifier());
        certifiers.put("cookware", new BasicCertifier());
        certifiers.put("photography", new PhotographyCertifier());
        certifiers.put("gardening", new BasicCertifier());
    }

    public List<hipstershop.Demo.Ad> certify(String category, List<hipstershop.Demo.Ad> ads) {
        if(!enabled){
            return ads;

         }
        logger.info("Getting ads for category: {}", category);
        Certifier certifier = certifiers.get(category);

        return certifier != null ? certifier.certify(ads) : ads;
    }
}
