export type Role = "user" | "assistant";

export interface Message {
  id: string;
  role: Role;
  content: string;
  tokensUsed?: number;
  error?: boolean;
}
