package hipstershop.copyright;

import hipstershop.AdService;
import hipstershop.Demo;
import java.util.Random;
import org.apache.logging.log4j.LogManager;
import org.apache.logging.log4j.Logger;

import java.util.LinkedList;
import java.util.List;

// A massive repository of very many photos.
public class StockPhotos {

    private final static long MAX_TIME_MS = 6500;
    private static final Logger logger = LogManager.getLogger(AdService.class);

    boolean isCopyright(Demo.Ad ad){
        boolean result = true;
        logger.info("Copyright check");
        long start = System.currentTimeMillis();
        for (CopyrightPhoto photo : createDatabase()) {
            if(photo.matchesAd(ad)){
                result = false;
            }
            if(System.currentTimeMillis() - start > MAX_TIME_MS){
                logger.warn("Copyright check exceeded max threshold. Aborting.");
                break;
            }
        }
        return result;
    }

    private static List<CopyrightPhoto> createDatabase() {
        List<CopyrightPhoto> result = new LinkedList<>();
        for(int i=0; i < 5000; i++){
            result.add(new CopyrightPhoto("photo" + i));
        }
        return result;
    }

    static class CopyrightPhoto {

        private final String id;
        private final byte[] photoFingerprint;
        private static final int MAX_FINGERPRINT_SIZE = Integer.parseInt(System.getenv().getOrDefault("STOCK_PHOTO_MAX_FINGERPRINT_SIZE", "1000000"));

        static {
            logger.info("photo size selected : {}", MAX_FINGERPRINT_SIZE);
        }

        public CopyrightPhoto(String id) {
            this.id = id;
            photoFingerprint = new byte[new Random().nextInt(MAX_FINGERPRINT_SIZE)];
        }

        public boolean matchesAd(Demo.Ad ad) {
            boolean matches = false;
            for (CopyrightPhoto photo : createDatabase()) {
                if(photo.id.equals(ad.getRedirectUrl())) {
                    matches = true;
                }
            }
            return matches;
        }
    }

}
