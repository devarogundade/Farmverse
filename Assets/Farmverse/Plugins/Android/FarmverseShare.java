package com.farmverse.nativebridge;

import android.app.Activity;
import android.content.Intent;
import android.net.Uri;

import androidx.core.content.FileProvider;

import java.io.File;
import java.io.FileOutputStream;
import java.io.OutputStreamWriter;
import java.io.Writer;
import java.nio.charset.StandardCharsets;

/**
 * The share sheet for a text file (the data export): write it under the app's cache,
 * hand it out through the FileProvider declared in FarmverseNative.androidlib, and
 * let the player pick an app. Answers once the chooser is shown.
 */
public final class FarmverseShare {
    private FarmverseShare() {}

    public static void shareText(final Activity activity, final String fileName, final String mimeType,
                                 final String content, final String title, final FarmverseCallback callback) {
        activity.runOnUiThread(() -> {
            try {
                File folder = new File(activity.getCacheDir(), "exports");
                if (!folder.exists() && !folder.mkdirs()) throw new IllegalStateException("cannot create " + folder);
                File file = new File(folder, new File(fileName).getName());
                try (Writer writer = new OutputStreamWriter(new FileOutputStream(file), StandardCharsets.UTF_8)) {
                    writer.write(content);
                }
                Uri uri = FileProvider.getUriForFile(activity, activity.getPackageName() + ".farmverse.fileprovider", file);
                Intent send = new Intent(Intent.ACTION_SEND);
                send.setType(mimeType);
                send.putExtra(Intent.EXTRA_STREAM, uri);
                send.putExtra(Intent.EXTRA_SUBJECT, title);
                send.addFlags(Intent.FLAG_GRANT_READ_URI_PERMISSION);
                activity.startActivity(Intent.createChooser(send, title));
                callback.onSuccess("");
            } catch (Exception error) {
                callback.onError("failed", String.valueOf(error.getMessage()));
            }
        });
    }
}
