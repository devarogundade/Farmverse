package com.farmverse.nativebridge;

import android.app.Activity;
import android.os.CancellationSignal;

import androidx.core.content.ContextCompat;
import androidx.credentials.Credential;
import androidx.credentials.CredentialManager;
import androidx.credentials.CredentialManagerCallback;
import androidx.credentials.CustomCredential;
import androidx.credentials.GetCredentialRequest;
import androidx.credentials.GetCredentialResponse;
import androidx.credentials.exceptions.GetCredentialCancellationException;
import androidx.credentials.exceptions.GetCredentialException;
import androidx.credentials.exceptions.NoCredentialException;

import com.google.android.libraries.identity.googleid.GetSignInWithGoogleOption;
import com.google.android.libraries.identity.googleid.GoogleIdTokenCredential;

/**
 * "Sign in with Google" through Android's Credential Manager, for one thing only: a
 * Google ID token that Firebase Auth links to the player's anonymous account. The
 * server client id is the project's web client (default_web_client_id, generated
 * from google-services.json by the Firebase Unity SDK).
 */
public final class FarmverseGoogleSignIn {
    private FarmverseGoogleSignIn() {}

    public static void signIn(final Activity activity, final FarmverseCallback callback) {
        activity.runOnUiThread(() -> {
            int resource = activity.getResources().getIdentifier("default_web_client_id", "string", activity.getPackageName());
            if (resource == 0) {
                callback.onError("unavailable", "default_web_client_id missing: add an OAuth web client to the Firebase project and refresh google-services.json");
                return;
            }
            GetSignInWithGoogleOption option = new GetSignInWithGoogleOption.Builder(activity.getString(resource)).build();
            GetCredentialRequest request = new GetCredentialRequest.Builder().addCredentialOption(option).build();
            CredentialManager.create(activity).getCredentialAsync(activity, request, new CancellationSignal(),
                ContextCompat.getMainExecutor(activity),
                new CredentialManagerCallback<GetCredentialResponse, GetCredentialException>() {
                    @Override
                    public void onResult(GetCredentialResponse response) {
                        Credential credential = response.getCredential();
                        if (credential instanceof CustomCredential
                            && GoogleIdTokenCredential.TYPE_GOOGLE_ID_TOKEN_CREDENTIAL.equals(credential.getType())) {
                            try {
                                callback.onSuccess(GoogleIdTokenCredential.createFrom(credential.getData()).getIdToken());
                            } catch (Exception error) {
                                callback.onError("failed", String.valueOf(error.getMessage()));
                            }
                        } else {
                            callback.onError("failed", "unexpected credential type " + credential.getType());
                        }
                    }

                    @Override
                    public void onError(GetCredentialException error) {
                        if (error instanceof GetCredentialCancellationException) callback.onError("cancelled", "cancelled");
                        else if (error instanceof NoCredentialException) callback.onError("unavailable", String.valueOf(error.getMessage()));
                        else callback.onError("failed", error.getType() + ": " + error.getMessage());
                    }
                });
        });
    }
}
